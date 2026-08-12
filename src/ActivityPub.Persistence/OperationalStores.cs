using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class DomainPolicyService(IDbContextFactory<FederationDbContext> contextFactory) : IDomainPolicyService
{
    public async Task<FederationPolicyKind> GetEffectivePolicyAsync(
        string domain,
        string? actorIri,
        CancellationToken cancellationToken)
    {
        string normalized = new System.Globalization.IdnMapping().GetAscii(domain.Trim().TrimEnd('.')).ToLowerInvariant();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (actorIri is not null)
        {
            bool actorBlocked = await db.ActorPolicies
                .AnyAsync(
                    x => x.ActorIri == actorIri && x.Kind == ModerationActionKind.BlockActor &&
                        x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now),
                    cancellationToken)
                .ConfigureAwait(false);
            if (actorBlocked)
            {
                return FederationPolicyKind.Reject;
            }
        }

        List<DomainPolicy> policies = await db.DomainPolicies
            .Where(x => x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        DomainPolicy? matched = policies
            .Where(x => normalized == x.Domain || normalized.EndsWith('.' + x.Domain, StringComparison.Ordinal))
            .OrderByDescending(x => x.Domain.Length)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        return matched?.Kind ?? FederationPolicyKind.Allow;
    }

    public async Task<IReadOnlySet<string>> FindRejectedActorsAsync(
        IReadOnlyCollection<string> actorIris,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorIris);
        string[] candidates = actorIris.Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string[] blockedActors = await db.ActorPolicies
            .Where(x => candidates.Contains(x.ActorIri) && x.Kind == ModerationActionKind.BlockActor &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => x.ActorIri)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        List<DomainPolicy> policies = await db.DomainPolicies
            .Where(x => x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rejected = new HashSet<string>(blockedActors, StringComparer.Ordinal);
        foreach (string actorIri in candidates)
        {
            string domain = new Uri(actorIri).IdnHost.ToLowerInvariant();
            DomainPolicy? matched = policies
                .Where(x => domain == x.Domain || domain.EndsWith('.' + x.Domain, StringComparison.Ordinal))
                .OrderByDescending(x => x.Domain.Length)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            if (matched?.Kind == FederationPolicyKind.Reject)
            {
                rejected.Add(actorIri);
            }
        }

        return rejected;
    }

    public async Task<IReadOnlySet<string>> FindRejectedActorsForLocalAsync(
        string localActorIri,
        IReadOnlyCollection<string> actorIris,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> global = await FindRejectedActorsAsync(actorIris, cancellationToken).ConfigureAwait(false);
        string[] candidates = actorIris.Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
        {
            return global;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string[] userBlocked = await db.UserBlocks.Where(x =>
                (x.OwnerActorIri == localActorIri && candidates.Contains(x.TargetActorIri) ||
                 x.TargetActorIri == localActorIri && candidates.Contains(x.OwnerActorIri)) &&
                x.State == FederatedRelationState.Active)
            .Select(x => x.OwnerActorIri == localActorIri ? x.TargetActorIri : x.OwnerActorIri)
            .Distinct()
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return global.Concat(userBlocked).ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed class WorkerHeartbeatStore(IDbContextFactory<FederationDbContext> contextFactory) : IWorkerHeartbeatStore
{
    public async Task RecordAsync(
        string workerId,
        string workerType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerType);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO activitypub.worker_heartbeats (worker_id, worker_type, last_seen_at)
            VALUES ({workerId}, {workerType}, {now})
            ON CONFLICT (worker_id, worker_type)
            DO UPDATE SET last_seen_at = EXCLUDED.last_seen_at
            """, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasRecentHeartbeatAsync(
        string workerType,
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.WorkerHeartbeats.AnyAsync(
            x => x.WorkerType == workerType && x.LastSeenAt >= threshold,
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PostgreSqlAuditLog(IDbContextFactory<FederationDbContext> contextFactory) : IAuditLog
{
    private const long AuditAdvisoryLock = 4_165_550_803_371_912_001;

    public async Task AppendAsync(
        string category,
        string action,
        string actor,
        string target,
        string detailsJson,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using JsonDocument _ = JsonDocument.Parse(detailsJson, new JsonDocumentOptions { MaxDepth = 32 });
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AuditAdvisoryLock})", cancellationToken).ConfigureAwait(false);
        string? previousHash = await db.AuditEvents
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => x.EventHash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        db.AuditEvents.Add(AuditEvent.Create(category, action, actor, target, detailsJson, previousHash, now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SchemaCompatibilityStore(IDbContextFactory<FederationDbContext> contextFactory) : ISchemaCompatibilityStore
{
    public async Task<SchemaCompatibilityResult> CheckAsync(string applicationVersion, CancellationToken cancellationToken)
    {
        if (!Version.TryParse(applicationVersion, out Version? current))
        {
            throw new ArgumentException("Application version is invalid.", nameof(applicationVersion));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SchemaCompatibility row = await db.SchemaCompatibility.SingleAsync(x => x.Id == 1, cancellationToken).ConfigureAwait(false);
        if (!Version.TryParse(row.MinimumApplicationVersion, out Version? minimum) ||
            !Version.TryParse(row.MaximumApplicationVersion, out Version? maximum))
        {
            throw new InvalidOperationException("Database schema compatibility versions are malformed.");
        }

        return new(row.MinimumApplicationVersion, row.MaximumApplicationVersion, current >= minimum && current <= maximum);
    }
}
