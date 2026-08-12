using ActivityPub.Application;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class AnnounceChainGuard(IDbContextFactory<FederationDbContext> contextFactory)
    : IAnnounceChainGuard
{
    private const int MaximumAnnounceChainDepth = 10;

    public async Task<bool> IsWithinChainLimitAsync(string objectIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int depth = 0;
        string current = objectIri;
        while (depth < MaximumAnnounceChainDepth)
        {
            string? nested = await db.Activities.AsNoTracking()
                .Where(activity => activity.Iri == current && activity.Type == "Announce")
                .Select(activity => activity.ObjectIri)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (nested is null)
            {
                return true;
            }

            current = nested;
            depth += 1;
        }

        return false;
    }
}
