using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RawJsonRetentionStore(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClock clock) : IRawJsonRetentionStore
{
    private const long AuditAdvisoryLock = 4_165_550_803_371_912_001;

    public async Task<RawJsonPurgeResult> PurgeBatchAsync(
        DateTimeOffset activityBefore,
        DateTimeOffset objectBefore,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        int activities = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS (
                SELECT a.id
                FROM activitypub.activities AS a
                WHERE a.direction = 'Inbound'
                  AND a.received_at < {activityBefore}
                  AND a.audit_raw_json IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM activitypub.legal_holds AS h
                      WHERE h.resource_kind = 'Activity'
                        AND h.resource_id = a.id
                        AND h.released_at IS NULL
                        AND (h.expires_at IS NULL OR h.expires_at > {now}))
                ORDER BY a.received_at, a.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE activitypub.activities AS a
            SET audit_raw_json = NULL, raw_json_purged_at = {now}
            FROM candidates
            WHERE a.id = candidates.id
            """, cancellationToken).ConfigureAwait(false);
        int objects = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS (
                SELECT o.id
                FROM activitypub.objects AS o
                WHERE o.updated_at < {objectBefore}
                  AND o.audit_raw_json IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM activitypub.local_actors AS l WHERE l.iri = o.owner_iri)
                  AND NOT EXISTS (
                      SELECT 1 FROM activitypub.legal_holds AS h
                      WHERE h.resource_kind = 'FederatedObject'
                        AND h.resource_id = o.id
                        AND h.released_at IS NULL
                        AND (h.expires_at IS NULL OR h.expires_at > {now}))
                ORDER BY o.updated_at, o.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE activitypub.objects AS o
            SET audit_raw_json = NULL, raw_json_purged_at = {now}
            FROM candidates
            WHERE o.id = candidates.id
            """, cancellationToken).ConfigureAwait(false);
        int revisions = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS (
                SELECT r.id
                FROM activitypub.object_revisions AS r
                JOIN activitypub.objects AS o ON o.id = r.object_id
                WHERE r.captured_at < {objectBefore}
                  AND r.audit_raw_json IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM activitypub.local_actors AS l WHERE l.iri = o.owner_iri)
                  AND NOT EXISTS (
                      SELECT 1 FROM activitypub.legal_holds AS h
                      WHERE h.resource_kind = 'FederatedObject'
                        AND h.resource_id = r.object_id
                        AND h.released_at IS NULL
                        AND (h.expires_at IS NULL OR h.expires_at > {now}))
                ORDER BY r.captured_at, r.id
                LIMIT {batchSize}
                FOR UPDATE OF r SKIP LOCKED
            )
            UPDATE activitypub.object_revisions AS r
            SET audit_raw_json = NULL, raw_json_purged_at = {now}
            FROM candidates
            WHERE r.id = candidates.id
            """, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(activities, objects, revisions);
    }

    public async Task<Guid> PlaceLegalHoldAsync(
        RawJsonResourceKind resourceKind,
        Guid resourceId,
        string reason,
        string operatorId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        LegalHold hold = LegalHold.Place(resourceKind, resourceId, reason, operatorId, now, expiresAt);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        bool exists = resourceKind == RawJsonResourceKind.Activity
            ? await db.Activities.AnyAsync(x => x.Id == resourceId && x.AuditRawJson != null, cancellationToken).ConfigureAwait(false)
            : await db.Objects.AnyAsync(x => x.Id == resourceId && x.AuditRawJson != null, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new KeyNotFoundException("Raw JSON resource does not exist or was already purged.");
        }

        db.LegalHolds.Add(hold);
        await AppendAuditAsync(
            db,
            "retention",
            "legal-hold-placed",
            operatorId,
            resourceId.ToString("N"),
            JsonSerializer.Serialize(new { hold.Id, resourceKind, expiresAt, reason }),
            now,
            cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return hold.Id;
    }

    public async Task<bool> ReleaseLegalHoldAsync(Guid holdId, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        LegalHold? hold = await db.LegalHolds.SingleOrDefaultAsync(x => x.Id == holdId, cancellationToken).ConfigureAwait(false);
        if (hold is null || hold.ReleasedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        hold.Release(operatorId, now);
        await AppendAuditAsync(
            db,
            "retention",
            "legal-hold-released",
            operatorId,
            hold.ResourceId.ToString("N"),
            JsonSerializer.Serialize(new { hold.Id, hold.ResourceKind }),
            now,
            cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<LegalHoldSummary>> ListLegalHoldsAsync(
        bool activeOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<LegalHold> query = db.LegalHolds;
        if (activeOnly)
        {
            query = query.Where(x => x.ReleasedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now));
        }

        return await query.OrderByDescending(x => x.PlacedAt).ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new LegalHoldSummary(
                x.Id,
                x.ResourceKind,
                x.ResourceId,
                x.Reason,
                x.PlacedBy,
                x.PlacedAt,
                x.ExpiresAt,
                x.ReleasedAt,
                x.ReleasedBy))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AppendAuditAsync(
        FederationDbContext db,
        string category,
        string action,
        string actor,
        string target,
        string detailsJson,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using JsonDocument _ = JsonDocument.Parse(detailsJson, new JsonDocumentOptions { MaxDepth = 32 });
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AuditAdvisoryLock})",
            cancellationToken).ConfigureAwait(false);
        string? previousHash = await db.AuditEvents
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.EventHash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        db.AuditEvents.Add(AuditEvent.Create(category, action, actor, target, detailsJson, previousHash, now));
    }
}
