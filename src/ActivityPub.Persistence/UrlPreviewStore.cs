using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class UrlPreviewStore(IDbContextFactory<FederationDbContext> contextFactory) : IUrlPreviewRepository
{
    public async Task<UrlPreview?> FindByUrlAsync(string url, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.UrlPreviews.AsNoTracking()
            .SingleOrDefaultAsync(preview => preview.Url == url, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(UrlPreview preview, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        UrlPreview? existing = await db.UrlPreviews.AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Url == preview.Url, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.UrlPreviews.Add(preview);
        }
        else
        {
            existing.Replace(preview);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
