using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RemoteKeyCacheStore(IDbContextFactory<FederationDbContext> contextFactory) : IRemoteKeyCacheStore
{
    public async Task<RemoteKeyCacheEntry?> FindAsync(string keyIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.RemoteKeyCache
            .Where(x => x.KeyIri == keyIri)
            .Select(x => new RemoteKeyCacheEntry(
                x.KeyIri,
                x.OwnerIri,
                x.PublicKeyPem,
                x.Algorithm,
                x.ExpiresAt,
                x.RefreshBlockedUntil))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(
        RemoteKeyCacheEntry entry,
        string sourceDocumentHash,
        DateTimeOffset fetchedAt,
        TimeSpan refreshCooldown,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        RemoteKeyCache? existing = await db.RemoteKeyCache.SingleOrDefaultAsync(x => x.KeyIri == entry.KeyIri, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.RemoteKeyCache.Add(RemoteKeyCache.Create(
                entry.KeyIri,
                entry.OwnerIri,
                entry.PublicKeyPem,
                entry.Algorithm,
                sourceDocumentHash,
                fetchedAt,
                entry.ExpiresAt));
        }
        else
        {
            existing.Refresh(
                entry.OwnerIri,
                entry.PublicKeyPem,
                entry.Algorithm,
                sourceDocumentHash,
                fetchedAt,
                entry.ExpiresAt,
                refreshCooldown);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
