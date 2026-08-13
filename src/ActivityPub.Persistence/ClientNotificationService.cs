using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class ClientNotificationService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClientApiQueryService clientQuery,
    IClientProjectionCache projectionCache) : IClientNotificationService
{
    public async Task<ClientPage<ClientNotificationView>> ReadAsync(
        string recipientActorIri,
        ClientNotificationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientActorIri);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (query.MarkAsRead)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        }

        DateTimeOffset? before = query.BeforeId is null
            ? null
            : await db.UserNotifications.Where(x => x.Id == query.BeforeId && x.RecipientActorIri == recipientActorIri)
                .Select(x => (DateTimeOffset?)x.CreatedAt)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<UserNotification> source = db.UserNotifications.Where(x =>
            x.RecipientActorIri == recipientActorIri && x.DismissedAt == null);
        if (before is not null)
        {
            source = source.Where(x => x.CreatedAt < before.Value || x.CreatedAt == before.Value && x.Id.CompareTo(query.BeforeId!.Value) < 0);
        }

        if (query.UnreadOnly)
        {
            source = source.Where(x => x.ReadAt == null);
        }

        if (query.IncludeKinds is { Count: > 0 })
        {
            UserNotificationKind[] included = query.IncludeKinds.ToArray();
            source = source.Where(x => included.Contains(x.Kind));
        }

        if (query.ExcludeKinds is { Count: > 0 })
        {
            UserNotificationKind[] excluded = query.ExcludeKinds.ToArray();
            source = source.Where(x => !excluded.Contains(x.Kind));
        }

        string[] userMutedSources = await db.UserMutes.Where(x =>
                x.OwnerActorIri == recipientActorIri && x.HideNotifications && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow))
            .Select(x => x.TargetActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] administrativelySuppressedSources = await db.ActorPolicies.Where(x =>
                (x.Kind == ModerationActionKind.BlockActor || x.Kind == ModerationActionKind.MuteActor) &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow))
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] blockedSources = await db.UserBlocks.Where(x =>
                (x.OwnerActorIri == recipientActorIri || x.TargetActorIri == recipientActorIri) &&
                x.State == FederatedRelationState.Active)
            .Select(x => x.OwnerActorIri == recipientActorIri ? x.TargetActorIri : x.OwnerActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] hiddenSources = userMutedSources.Concat(administrativelySuppressedSources).Concat(blockedSources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        source = source.Where(x => !hiddenSources.Contains(x.SourceActorIri));
        List<UserNotification> rows = await source
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        if (query.MarkAsRead)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (UserNotification row in rows)
            {
                row.MarkRead(recipientActorIri, now);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count > 0)
            {
                await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var result = new List<ClientNotificationView>(rows.Count);
        foreach (UserNotification row in rows)
        {
            ClientNotificationView? mapped = await MapAsync(db, row, cancellationToken).ConfigureAwait(false);
            if (mapped is not null)
            {
                result.Add(mapped);
            }
        }

        return new(
            result,
            hasMore && rows.Count > 0 ? new(rows[^1].Id, rows[^1].CreatedAt) : null,
            rows.Count > 0 ? new(rows[0].Id, rows[0].CreatedAt) : null);
    }

    public async Task<ClientNotificationView?> FindAsync(
        string recipientActorIri,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        UserNotification? row = await db.UserNotifications.SingleOrDefaultAsync(x =>
            x.Id == id && x.RecipientActorIri == recipientActorIri && x.DismissedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (row is not null && await IsSourceSuppressedAsync(db, recipientActorIri, row.SourceActorIri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return row is null ? null : await MapAsync(db, row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkReadAsync(
        string recipientActorIri,
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ids.Count is < 1 or > 100)
        {
            return false;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        UserNotification[] rows = await db.UserNotifications.Where(x =>
            ids.Contains(x.Id) && x.RecipientActorIri == recipientActorIri && x.DismissedAt == null)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Length != ids.Distinct().Count())
        {
            return false;
        }

        foreach (UserNotification row in rows)
        {
            row.MarkRead(recipientActorIri, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<int> MarkAllReadAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int changed = await db.UserNotifications.Where(x =>
                x.RecipientActorIri == recipientActorIri && x.ReadAt == null && x.DismissedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReadAt, now), cancellationToken).ConfigureAwait(false);
        if (changed > 0)
        {
            await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
                .ConfigureAwait(false);
        }

        return changed;
    }

    public async Task<bool> DismissAsync(string recipientActorIri, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        UserNotification? row = await db.UserNotifications.SingleOrDefaultAsync(x =>
            x.Id == id && x.RecipientActorIri == recipientActorIri && x.DismissedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        row.Dismiss(recipientActorIri, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<int> ClearAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int changed = await db.UserNotifications.Where(x => x.RecipientActorIri == recipientActorIri && x.DismissedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ReadAt, x => x.ReadAt ?? now)
                .SetProperty(x => x.DismissedAt, now), cancellationToken).ConfigureAwait(false);
        if (changed > 0)
        {
            await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
                .ConfigureAwait(false);
        }

        return changed;
    }

    public async Task<long> CountUnreadAsync(string recipientActorIri, CancellationToken cancellationToken)
    {
        long? cached = await projectionCache.GetUnreadNotificationCountAsync(recipientActorIri, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.Value;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        long count = await db.UserNotifications.LongCountAsync(x =>
            x.RecipientActorIri == recipientActorIri && x.ReadAt == null && x.DismissedAt == null &&
            !db.UserMutes.Any(mute =>
                mute.OwnerActorIri == recipientActorIri && mute.TargetActorIri == x.SourceActorIri &&
                mute.HideNotifications && mute.RevokedAt == null &&
                (mute.ExpiresAt == null || mute.ExpiresAt > DateTimeOffset.UtcNow)) &&
            !db.ActorPolicies.Any(policy =>
                policy.ActorIri == x.SourceActorIri &&
                (policy.Kind == ModerationActionKind.BlockActor || policy.Kind == ModerationActionKind.MuteActor) &&
                policy.RevokedAt == null && (policy.ExpiresAt == null || policy.ExpiresAt > DateTimeOffset.UtcNow)) &&
            !db.UserBlocks.Any(block =>
                (block.OwnerActorIri == recipientActorIri && block.TargetActorIri == x.SourceActorIri ||
                 block.OwnerActorIri == x.SourceActorIri && block.TargetActorIri == recipientActorIri) &&
                block.State == FederatedRelationState.Active),
            cancellationToken).ConfigureAwait(false);
        await projectionCache.SetUnreadNotificationCountAsync(recipientActorIri, count, cancellationToken)
            .ConfigureAwait(false);
        return count;
    }

    private static async Task<bool> IsSourceSuppressedAsync(
        FederationDbContext db,
        string recipientActorIri,
        string sourceActorIri,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await db.UserMutes.AnyAsync(x =>
                x.OwnerActorIri == recipientActorIri && x.TargetActorIri == sourceActorIri && x.HideNotifications &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false) ||
            await db.ActorPolicies.AnyAsync(x =>
                x.ActorIri == sourceActorIri &&
                (x.Kind == ModerationActionKind.BlockActor || x.Kind == ModerationActionKind.MuteActor) &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false) ||
            await db.UserBlocks.AnyAsync(x =>
                (x.OwnerActorIri == recipientActorIri && x.TargetActorIri == sourceActorIri ||
                 x.OwnerActorIri == sourceActorIri && x.TargetActorIri == recipientActorIri) &&
                x.State == FederatedRelationState.Active,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientNotificationView?> MapAsync(
        FederationDbContext db,
        UserNotification row,
        CancellationToken cancellationToken)
    {
        ClientAccountView? account = await clientQuery.FindAccountByIriAsync(row.SourceActorIri, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        ClientPostView? post = null;
        if (row.ObjectIri is not null)
        {
            Guid? objectId = await db.Objects.Where(x => x.Iri == row.ObjectIri && !x.IsDeleted)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (objectId is not null)
            {
                post = await clientQuery.FindPostAsync(objectId.Value, row.RecipientActorIri, cancellationToken).ConfigureAwait(false);
            }
        }

        return new(row.Id, row.Kind, row.CreatedAt, row.ReadAt is not null, row.Reaction, account, post);
    }
}
