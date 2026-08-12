using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class RelayStore(FederationDbContext db) : IRelayRepository
{
    public Task<Relay?> FindByInboxAsync(string inbox, CancellationToken cancellationToken) =>
        db.Relays.SingleOrDefaultAsync(
            relay => relay.Inbox == inbox,
            cancellationToken);

    public Task<IReadOnlyList<Relay>> ListAsync(CancellationToken cancellationToken) =>
        db.Relays.AsNoTracking()
            .OrderBy(relay => relay.CreatedAt)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Relay>)task.Result, cancellationToken);

    public Task<IReadOnlyList<Relay>> ListAcceptedAsync(CancellationToken cancellationToken) =>
        db.Relays.AsNoTracking()
            .Where(relay => relay.Status == RelayStatus.Accepted)
            .OrderBy(relay => relay.CreatedAt)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Relay>)task.Result, cancellationToken);

    public Task AddAsync(Relay relay, CancellationToken cancellationToken)
    {
        db.Relays.Add(relay);
        return db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid relayId, RelayStatus status, CancellationToken cancellationToken)
    {
        Relay? relay = await db.Relays.AsTracking()
            .SingleOrDefaultAsync(x => x.Id == relayId, cancellationToken).ConfigureAwait(false);
        if (relay is null)
        {
            return;
        }

        if (status == RelayStatus.Accepted)
        {
            relay.Accept(DateTimeOffset.UtcNow);
        }
        else
        {
            relay.Reject(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid relayId, CancellationToken cancellationToken)
    {
        Relay? relay = await db.Relays.SingleOrDefaultAsync(x => x.Id == relayId, cancellationToken).ConfigureAwait(false);
        if (relay is not null)
        {
            db.Relays.Remove(relay);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
