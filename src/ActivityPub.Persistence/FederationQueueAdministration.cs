using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class FederationQueueAdministration(
    IDbContextFactory<FederationDbContext> contextFactory,
    IFederationQueueSignal queueSignal) : IFederationQueueAdministration
{
    public async Task<FederationQueueStats> GetStatsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        IQueryable<Delivery> deliveries = db.Deliveries.AsNoTracking();
        DateTimeOffset recentThreshold = now.AddSeconds(-10);
        long processedDeliveriesRecently = await db.DeliveryAttempts.LongCountAsync(
            item => item.CompletedAt >= recentThreshold,
            cancellationToken).ConfigureAwait(false);
        long waiting = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.Pending && item.AvailableAt <= now,
            cancellationToken).ConfigureAwait(false);
        long delayed = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.Pending && item.AvailableAt > now,
            cancellationToken).ConfigureAwait(false);
        long active = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.Leased && item.LeaseExpiresAt > now,
            cancellationToken).ConfigureAwait(false);
        long stalled = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.Leased && item.LeaseExpiresAt <= now,
            cancellationToken).ConfigureAwait(false);
        long deadLettered = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.DeadLettered,
            cancellationToken).ConfigureAwait(false);
        long cancelled = await deliveries.LongCountAsync(
            item => item.State == WorkItemState.Cancelled,
            cancellationToken).ConfigureAwait(false);
        IQueryable<InboxItem> inboxItems = db.InboxItems.AsNoTracking();
        long processedInboxItemsRecently = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.Succeeded && item.UpdatedAt >= recentThreshold,
            cancellationToken).ConfigureAwait(false);
        long inboxWaiting = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.Pending && item.AvailableAt <= now,
            cancellationToken).ConfigureAwait(false);
        long inboxDelayed = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.Pending && item.AvailableAt > now,
            cancellationToken).ConfigureAwait(false);
        long inboxActive = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.Leased && item.LeaseExpiresAt > now,
            cancellationToken).ConfigureAwait(false);
        long inboxStalled = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.Leased && item.LeaseExpiresAt <= now,
            cancellationToken).ConfigureAwait(false);
        long inboxDeadLettered = await inboxItems.LongCountAsync(
            item => item.State == WorkItemState.DeadLettered,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset? oldestDelivery = await deliveries
            .Where(item => item.State == WorkItemState.Pending || item.State == WorkItemState.Leased)
            .MinAsync(item => (DateTimeOffset?)item.CreatedAt, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? oldestInbox = await inboxItems
            .Where(item => item.State == WorkItemState.Pending || item.State == WorkItemState.Leased)
            .MinAsync(item => (DateTimeOffset?)item.CreatedAt, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? nextDelivery = await deliveries
            .Where(item => item.State == WorkItemState.Pending)
            .MinAsync(item => (DateTimeOffset?)item.AvailableAt, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? nextInbox = await inboxItems
            .Where(item => item.State == WorkItemState.Pending)
            .MinAsync(item => (DateTimeOffset?)item.AvailableAt, cancellationToken).ConfigureAwait(false);
        var delayedDomainRows = await deliveries
            .Where(item => item.State == WorkItemState.Pending && item.AvailableAt > now)
            .GroupBy(item => item.RemoteDomain)
            .Select(group => new { Domain = group.Key, Count = group.LongCount() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Domain)
            .Take(50)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        FederationQueueDomainCount[] delayedByDomain = delayedDomainRows
            .Select(item => new FederationQueueDomainCount(item.Domain, item.Count))
            .ToArray();
        FederationQueueDomainCount[] inboxDelayedByDomain = await db.Database
            .SqlQuery<FederationQueueDomainCount>($"""
                SELECT lower(split_part(split_part(actor_iri, '://', 2), '/', 1)) AS "Domain",
                       count(*)::bigint AS "Count"
                FROM activitypub.inbox_items
                WHERE state = 'Pending' AND available_at > {now}
                GROUP BY 1
                ORDER BY count(*) DESC, 1
                LIMIT 50
                """)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return new(
            processedDeliveriesRecently,
            waiting,
            active,
            delayed,
            stalled,
            deadLettered,
            cancelled,
            processedInboxItemsRecently,
            inboxWaiting,
            inboxActive,
            inboxDelayed,
            inboxStalled,
            inboxDeadLettered,
            Minimum(oldestDelivery, oldestInbox),
            Minimum(nextDelivery, nextInbox),
            queueSignal.IsEnabled,
            delayedByDomain,
            inboxDelayedByDomain);
    }

    public async Task<IReadOnlyList<FederationQueueJobSummary>> ListAsync(
        WorkItemState? state,
        bool? delayed,
        string? remoteDomain,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string? normalizedDomain = string.IsNullOrWhiteSpace(remoteDomain)
            ? null
            : new UriBuilder(Uri.UriSchemeHttps, remoteDomain.Trim()).Uri.IdnHost.ToLowerInvariant();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        IQueryable<Delivery> query = db.Deliveries.AsNoTracking();
        if (state is not null)
        {
            query = query.Where(item => item.State == state.Value);
        }

        if (delayed is not null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            query = delayed.Value
                ? query.Where(item => item.State == WorkItemState.Pending && item.AvailableAt > now)
                : query.Where(item => item.State == WorkItemState.Pending && item.AvailableAt <= now);
        }

        if (normalizedDomain is not null)
        {
            query = query.Where(item => item.RemoteDomain == normalizedDomain);
        }

        if (before is not null)
        {
            query = query.Where(item => item.CreatedAt < before.Value);
        }

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new FederationQueueJobSummary(
                item.Id,
                item.ActivityId,
                item.EndpointIri,
                item.RemoteDomain,
                item.State,
                item.AvailableAt,
                item.LeaseOwner,
                item.LeaseExpiresAt,
                item.AttemptCount,
                item.LastStatusCode,
                item.LastErrorCode,
                item.CreatedAt,
                item.UpdatedAt,
                item.CompletedAt))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FederationInboxJobSummary>> ListInboxAsync(
        WorkItemState? state,
        bool? delayed,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        IQueryable<InboxItem> query = db.InboxItems.AsNoTracking();
        if (state is not null)
        {
            query = query.Where(item => item.State == state.Value);
        }

        if (delayed is not null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            query = delayed.Value
                ? query.Where(item => item.State == WorkItemState.Pending && item.AvailableAt > now)
                : query.Where(item => item.State == WorkItemState.Pending && item.AvailableAt <= now);
        }

        if (before is not null)
        {
            query = query.Where(item => item.CreatedAt < before.Value);
        }

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new FederationInboxJobSummary(
                item.Id,
                item.ActivityIri,
                item.ActorIri,
                item.ActivityType,
                item.State,
                item.AvailableAt,
                item.LeaseOwner,
                item.LeaseExpiresAt,
                item.AttemptCount,
                item.LastErrorCode,
                item.CreatedAt,
                item.UpdatedAt,
                item.CompletedAt))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset? Minimum(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;
}
