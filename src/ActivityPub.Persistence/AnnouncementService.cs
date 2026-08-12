using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class AnnouncementService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClock clock) : IAnnouncementService
{
    private const long AuditAdvisoryLock = 4_165_550_803_371_912_001;

    public async Task<IReadOnlyList<AnnouncementView>> ReadAsync(
        AnnouncementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateLimit(query.Limit);
        string? viewerActorIri = query.ViewerActorIri is null
            ? null
            : CanonicalIri.RequireAbsoluteHttp(query.ViewerActorIri, nameof(query.ViewerActorIri));
        DateTimeOffset now = clock.UtcNow;

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Announcement> announcements = db.Announcements
            .AsNoTracking()
            .Where(value => value.DeletedAt == null && value.PublishedAt <= now &&
                (value.ExpiresAt == null || value.ExpiresAt > now));
        announcements = viewerActorIri is null
            ? announcements.Where(value => value.Audience == AnnouncementAudience.Public)
            : announcements.Where(value => value.Audience == AnnouncementAudience.Public ||
                value.Audience == AnnouncementAudience.Authenticated);
        announcements = await ApplyCursorsAsync(
            db,
            announcements,
            query.SinceId,
            query.UntilId,
            cancellationToken).ConfigureAwait(false);

        if (query.WithUnreads && viewerActorIri is not null)
        {
            announcements = announcements.Where(value => !db.AnnouncementReads.Any(read =>
                read.AnnouncementId == value.Id && read.ReaderActorIri == viewerActorIri));
        }

        List<Announcement> values = await announcements
            .OrderByDescending(value => value.SortOrdinal)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] announcementIds = values.Select(value => value.Id).ToArray();
        HashSet<Guid> reads = viewerActorIri is null || values.Count == 0
            ? []
            : (await db.AnnouncementReads
                .AsNoTracking()
                .Where(value => value.ReaderActorIri == viewerActorIri &&
                    announcementIds.Contains(value.AnnouncementId))
                .Select(value => value.AnnouncementId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToHashSet();

        return values.Select(value => ToView(
            value,
            viewerActorIri is null ? null : reads.Contains(value.Id),
            reads: 0)).ToArray();
    }

    public async Task<IReadOnlyList<AnnouncementView>> ReadForAdministrationAsync(
        Guid? sinceId,
        Guid? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Announcement> announcements = db.Announcements.AsNoTracking().Where(value => value.DeletedAt == null);
        announcements = await ApplyCursorsAsync(db, announcements, sinceId, untilId, cancellationToken).ConfigureAwait(false);
        List<Announcement> values = await announcements
            .OrderByDescending(value => value.SortOrdinal)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] ids = values.Select(value => value.Id).ToArray();
        Dictionary<Guid, long> reads = ids.Length == 0
            ? []
            : await db.AnnouncementReads
                .AsNoTracking()
                .Where(value => ids.Contains(value.AnnouncementId))
                .GroupBy(value => value.AnnouncementId)
                .ToDictionaryAsync(group => group.Key, group => group.LongCount(), cancellationToken)
                .ConfigureAwait(false);
        return values.Select(value => ToView(value, isRead: null, reads.GetValueOrDefault(value.Id))).ToArray();
    }

    public async Task<AnnouncementView> CreateAsync(
        AnnouncementMutation mutation,
        string operatorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        DateTimeOffset now = clock.UtcNow;
        Announcement announcement = Announcement.Create(
            mutation.Title,
            mutation.Text,
            mutation.ImageUrl,
            mutation.Audience ?? AnnouncementAudience.Public,
            mutation.PublishedAt ?? now,
            mutation.ExpiresAt,
            operatorId,
            now);

        await using FederationDbContext db = await TrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        db.Announcements.Add(announcement);
        await AppendAuditAsync(db, "create", announcement, operatorId, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToView(announcement, isRead: null, reads: 0);
    }

    public async Task<AnnouncementView?> UpdateAsync(
        Guid id,
        AnnouncementMutation mutation,
        string operatorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await TrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        Announcement? announcement = await db.Announcements
            .SingleOrDefaultAsync(value => value.Id == id && value.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (announcement is null)
        {
            return null;
        }

        announcement.Update(
            mutation.Title,
            mutation.Text,
            mutation.ImageUrl,
            mutation.Audience ?? announcement.Audience,
            mutation.PublishedAt ?? announcement.PublishedAt,
            mutation.ReplaceExpiresAt ? mutation.ExpiresAt : announcement.ExpiresAt,
            operatorId,
            now);
        await AppendAuditAsync(db, "update", announcement, operatorId, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        long reads = await db.AnnouncementReads.LongCountAsync(
            value => value.AnnouncementId == announcement.Id,
            cancellationToken).ConfigureAwait(false);
        return ToView(announcement, isRead: null, reads);
    }

    public async Task<bool> DeleteAsync(Guid id, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await TrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        Announcement? announcement = await db.Announcements
            .SingleOrDefaultAsync(value => value.Id == id && value.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (announcement is null)
        {
            return false;
        }

        announcement.Delete(operatorId, now);
        await AppendAuditAsync(db, "delete", announcement, operatorId, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> MarkReadAsync(
        Guid id,
        string readerActorIri,
        CancellationToken cancellationToken)
    {
        string actorIri = CanonicalIri.RequireAbsoluteHttp(readerActorIri, nameof(readerActorIri));
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool visible = await db.Announcements.AsNoTracking().AnyAsync(value =>
            value.Id == id && value.DeletedAt == null && value.PublishedAt <= now &&
            (value.ExpiresAt == null || value.ExpiresAt > now) &&
            (value.Audience == AnnouncementAudience.Public || value.Audience == AnnouncementAudience.Authenticated),
            cancellationToken).ConfigureAwait(false);
        if (!visible)
        {
            return false;
        }

        AnnouncementRead read = AnnouncementRead.Create(id, actorIri, now);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO activitypub.announcement_reads (id, announcement_id, reader_actor_iri, created_at)
            VALUES ({read.Id}, {read.AnnouncementId}, {read.ReaderActorIri}, {read.CreatedAt})
            ON CONFLICT (announcement_id, reader_actor_iri) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<FederationDbContext> TrackingContextAsync(CancellationToken cancellationToken)
    {
        FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        return db;
    }

    private static async Task<IQueryable<Announcement>> ApplyCursorsAsync(
        FederationDbContext db,
        IQueryable<Announcement> query,
        Guid? sinceId,
        Guid? untilId,
        CancellationToken cancellationToken)
    {
        if (sinceId is not null)
        {
            long? ordinal = await db.Announcements.AsNoTracking()
                .Where(value => value.Id == sinceId.Value)
                .Select(value => (long?)value.SortOrdinal)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (ordinal is null)
            {
                throw new KeyNotFoundException("Announcement cursor does not exist.");
            }

            query = query.Where(value => value.SortOrdinal > ordinal.Value);
        }

        if (untilId is not null)
        {
            long? ordinal = await db.Announcements.AsNoTracking()
                .Where(value => value.Id == untilId.Value)
                .Select(value => (long?)value.SortOrdinal)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (ordinal is null)
            {
                throw new KeyNotFoundException("Announcement cursor does not exist.");
            }

            query = query.Where(value => value.SortOrdinal < ordinal.Value);
        }

        return query;
    }

    private static async Task AppendAuditAsync(
        FederationDbContext db,
        string action,
        Announcement announcement,
        string operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AuditAdvisoryLock})",
            cancellationToken).ConfigureAwait(false);
        string? previousHash = await db.AuditEvents
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Select(value => value.EventHash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        string details = JsonSerializer.Serialize(new
        {
            announcement.Audience,
            announcement.PublishedAt,
            announcement.ExpiresAt,
            HasImage = announcement.ImageUrl is not null,
            announcement.Version
        });
        db.AuditEvents.Add(AuditEvent.Create(
            "announcement",
            action,
            operatorId,
            announcement.Id.ToString("D"),
            details,
            previousHash,
            now));
    }

    private static AnnouncementView ToView(Announcement value, bool? isRead, long reads) => new(
        value.Id,
        value.SortOrdinal,
        value.CreatedAt,
        value.UpdatedAt,
        value.Title,
        value.Text,
        value.ImageUrl,
        value.Audience,
        value.PublishedAt,
        value.ExpiresAt,
        isRead,
        reads);

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Announcement limits must be between 1 and 100.");
        }
    }
}
