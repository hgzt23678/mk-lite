using ActivityPub.Application;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RemoteDomainExecutionStore(IDbContextFactory<FederationDbContext> contextFactory) : IRemoteDomainExecutionStore
{
    public async Task<DomainLeaseToken?> TryAcquireAsync(
        string domain,
        string owner,
        Guid deliveryId,
        int maximumSlots,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        Validate(domain, owner, maximumSlots);
        DateTimeOffset expiresAt = now.Add(duration);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ActivityPub.Domain.FederationPolicyKind? effectivePolicy = await db.DomainPolicies
            .Where(policy => (policy.Domain == domain || EF.Functions.Like(domain, "%." + policy.Domain)) &&
                policy.RevokedAt == null && (policy.ExpiresAt == null || policy.ExpiresAt > now))
            .OrderByDescending(policy => policy.Domain.Length)
            .ThenByDescending(policy => policy.CreatedAt)
            .Select(policy => (ActivityPub.Domain.FederationPolicyKind?)policy.Kind)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (effectivePolicy == ActivityPub.Domain.FederationPolicyKind.Limit)
        {
            maximumSlots = 1;
        }

        for (int slot = 1; slot <= maximumSlots; slot++)
        {
            int changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO activitypub.domain_delivery_leases (domain, slot, owner, delivery_id, expires_at)
                VALUES ({domain}, {slot}, {owner}, {deliveryId}, {expiresAt})
                ON CONFLICT (domain, slot) DO UPDATE
                SET owner = EXCLUDED.owner,
                    delivery_id = EXCLUDED.delivery_id,
                    expires_at = EXCLUDED.expires_at
                WHERE activitypub.domain_delivery_leases.expires_at <= {now}
                """, cancellationToken).ConfigureAwait(false);
            if (changed == 1)
            {
                return new(domain, slot, owner, deliveryId, expiresAt);
            }
        }

        return null;
    }

    public async Task ExtendAsync(
        DomainLeaseToken token,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset expiresAt = now.Add(duration);
        int changed = await db.DomainDeliveryLeases
            .Where(x => x.Domain == token.Domain && x.Slot == token.Slot && x.Owner == token.Owner &&
                x.DeliveryId == token.DeliveryId && x.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt, expiresAt), cancellationToken)
            .ConfigureAwait(false);
        if (changed != 1)
        {
            throw new InvalidOperationException("Remote-domain concurrency lease was lost.");
        }
    }

    public async Task ReleaseAsync(DomainLeaseToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.DomainDeliveryLeases
            .Where(x => x.Domain == token.Domain && x.Slot == token.Slot && x.Owner == token.Owner && x.DeliveryId == token.DeliveryId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetCircuitOpenUntilAsync(
        string domain,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.RemoteDomainCircuits
            .Where(x => x.Domain == domain && x.OpenUntil > now)
            .Select(x => x.OpenUntil)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordCircuitSuccessAsync(string domain, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO activitypub.remote_domain_circuits (domain, consecutive_failures, open_until, updated_at)
            VALUES ({domain}, 0, NULL, {now})
            ON CONFLICT (domain) DO UPDATE
            SET consecutive_failures = 0, open_until = NULL, updated_at = EXCLUDED.updated_at
            """, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordCircuitFailureAsync(
        string domain,
        DateTimeOffset now,
        int threshold,
        TimeSpan breakDuration,
        CancellationToken cancellationToken)
    {
        DateTimeOffset openUntil = now.Add(breakDuration);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO activitypub.remote_domain_circuits (domain, consecutive_failures, open_until, updated_at)
            VALUES ({domain}, 1, NULL, {now})
            ON CONFLICT (domain) DO UPDATE
            SET consecutive_failures = activitypub.remote_domain_circuits.consecutive_failures + 1,
                open_until = CASE
                    WHEN activitypub.remote_domain_circuits.consecutive_failures + 1 >= {threshold} THEN {openUntil}
                    ELSE activitypub.remote_domain_circuits.open_until
                END,
                updated_at = EXCLUDED.updated_at
            """, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(string domain, string owner, int maximumSlots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (domain.Length > 255 || owner.Length > 256 || maximumSlots is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSlots), "Remote-domain lease input is outside supported bounds.");
        }
    }
}
