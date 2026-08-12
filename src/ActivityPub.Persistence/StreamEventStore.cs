using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class StreamEventStore(IDbContextFactory<FederationDbContext> contextFactory) : IStreamEventStore
{
    public async Task<StreamEventPage> ReadAfterAsync(
        long afterCursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);

        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        long? oldest = await db.StreamEvents.MinAsync(item => (long?)item.Cursor, cancellationToken).ConfigureAwait(false);
        long? latest = await db.StreamEvents.MaxAsync(item => (long?)item.Cursor, cancellationToken).ConfigureAwait(false);
        if (afterCursor > 0 && oldest is not null && afterCursor < oldest.Value - 1)
        {
            return new([], oldest, latest, RequestedCursorExpired: true);
        }

        StreamEvent[] events = await db.StreamEvents.AsNoTracking()
            .Where(item => item.Cursor > afterCursor)
            .OrderBy(item => item.Cursor)
            .Take(limit)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(events, oldest, latest, RequestedCursorExpired: false);
    }
}
