using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class HashtagStore(IDbContextFactory<FederationDbContext> contextFactory) : IHashtagRepository
{
    private static readonly TimeSpan TrendWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ChartInterval = TimeSpan.FromMinutes(10);
    private const int ChartBuckets = 20;
    private const int TrendLimit = 5;

    public async Task RecordUsageAsync(
        IReadOnlyList<string> names,
        string ownerIri,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string[] distinct = names
            .Select(name => name.TrimStart('#').ToLowerInvariant())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string name in distinct)
        {
            Hashtag? existing = await db.Hashtags.AsTracking()
                .SingleOrDefaultAsync(tag => tag.Name == name, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                db.Hashtags.Add(Hashtag.Create(name, now));
            }
            else
            {
                existing.RecordUsage(now);
            }

            db.HashtagUsages.Add(HashtagUsage.Create(name, ownerIri, now));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        string prefix = query.TrimStart('#').ToLowerInvariant();
        if (prefix.Length == 0)
        {
            return [];
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Hashtags.AsNoTracking()
            .Where(tag => tag.Name.StartsWith(prefix))
            .OrderByDescending(tag => tag.Count)
            .OrderBy(tag => tag.Name)
            .Skip(offset)
            .Take(limit)
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HashtagTrend>> TrendAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset windowStart = now - TrendWindow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string[] hots = await db.HashtagUsages.AsNoTracking()
            .Where(usage => usage.UsedAt > windowStart)
            .GroupBy(usage => usage.Name)
            .OrderByDescending(group => group.Select(usage => usage.OwnerIri).Distinct().Count())
            .ThenBy(group => group.Key)
            .Take(TrendLimit)
            .Select(group => group.Key)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (hots.Length == 0)
        {
            return [];
        }

        var trends = new List<HashtagTrend>(hots.Length);
        foreach (string tag in hots)
        {
            long usersCount = await db.HashtagUsages.AsNoTracking()
                .Where(usage => usage.Name == tag && usage.UsedAt > windowStart)
                .Select(usage => usage.OwnerIri)
                .Distinct()
                .LongCountAsync(cancellationToken).ConfigureAwait(false);
            var chart = new List<long>(ChartBuckets);
            for (int index = 0; index < ChartBuckets; index++)
            {
                DateTimeOffset bucketEnd = now - (ChartInterval * index);
                DateTimeOffset bucketStart = now - (ChartInterval * (index + 1));
                long bucketUsers = await db.HashtagUsages.AsNoTracking()
                    .Where(usage => usage.Name == tag &&
                                    usage.UsedAt > bucketStart &&
                                    usage.UsedAt <= bucketEnd)
                    .Select(usage => usage.OwnerIri)
                    .Distinct()
                    .LongCountAsync(cancellationToken).ConfigureAwait(false);
                chart.Add(bucketUsers);
            }

            trends.Add(new HashtagTrend(tag, usersCount, chart));
        }

        return trends;
    }
}
