using System.Security.Cryptography;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class RemoteInstanceQueryService(
    IDbContextFactory<FederationDbContext> contextFactory) : IRemoteInstanceQueryService
{
    public async Task<IReadOnlyList<RemoteInstanceView>> ReadAsync(
        RemoteInstanceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Remote instance limit must be between 1 and 100.");
        }

        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Remote instance offset cannot be negative.");
        }

        string? hostFilter = string.IsNullOrWhiteSpace(query.Host) ? null : query.Host.Trim();
        if (hostFilter?.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Remote instance host filter is too long.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var actorRows = await db.RemoteActors
            .Where(actor => actor.GoneAt == null)
            .GroupBy(actor => actor.Origin)
            .Select(group => new
            {
                Origin = group.Key,
                CaughtAt = group.Min(actor => actor.FetchedAt),
                LastCommunicatedAt = group.Max(actor => actor.UpdatedAt),
                UsersCount = group.LongCount()
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var accumulators = new Dictionary<string, RemoteInstanceAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in actorRows)
        {
            string host = HostFromOrigin(row.Origin);
            if (hostFilter is not null && !host.Contains(hostFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RemoteInstanceAccumulator value = GetOrCreate(accumulators, host, row.CaughtAt, row.LastCommunicatedAt);
            value.UsersCount += row.UsersCount;
            value.CaughtAt = Earlier(value.CaughtAt, row.CaughtAt);
            value.LastCommunicatedAt = Later(value.LastCommunicatedAt, row.LastCommunicatedAt);
        }

        if (accumulators.Count == 0)
        {
            return [];
        }

        string[] origins = actorRows
            .Select(row => row.Origin)
            .Where(origin => accumulators.ContainsKey(HostFromOrigin(origin)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var noteRows = await (
            from item in db.Objects
            join actor in db.RemoteActors on item.OwnerIri equals actor.Iri
            where !item.IsDeleted && actor.GoneAt == null && origins.Contains(actor.Origin)
            group item by actor.Origin
            into grouped
            select new { Origin = grouped.Key, Count = grouped.LongCount() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in noteRows)
        {
            accumulators[HostFromOrigin(row.Origin)].NotesCount += row.Count;
        }

        var followingRows = await (
            from relation in db.FollowRelations
            join actor in db.RemoteActors on relation.FollowerIri equals actor.Iri
            where relation.State == FollowState.Accepted && actor.GoneAt == null && origins.Contains(actor.Origin)
            group relation by actor.Origin
            into grouped
            select new { Origin = grouped.Key, Count = grouped.LongCount() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in followingRows)
        {
            accumulators[HostFromOrigin(row.Origin)].FollowingCount += row.Count;
        }

        var followerRows = await (
            from relation in db.FollowRelations
            join actor in db.RemoteActors on relation.FollowedIri equals actor.Iri
            where relation.State == FollowState.Accepted && actor.GoneAt == null && origins.Contains(actor.Origin)
            group relation by actor.Origin
            into grouped
            select new { Origin = grouped.Key, Count = grouped.LongCount() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in followerRows)
        {
            accumulators[HostFromOrigin(row.Origin)].FollowersCount += row.Count;
        }

        var requestRows = await (
            from attempt in db.DeliveryAttempts
            join delivery in db.Deliveries on attempt.DeliveryId equals delivery.Id
            join target in db.DeliveryTargets on delivery.Id equals target.DeliveryId
            join actor in db.RemoteActors on target.ActorIri equals actor.Iri
            where actor.GoneAt == null && origins.Contains(actor.Origin)
            group attempt by actor.Origin
            into grouped
            select new { Origin = grouped.Key, SentAt = grouped.Max(attempt => attempt.StartedAt) })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in requestRows)
        {
            RemoteInstanceAccumulator value = accumulators[HostFromOrigin(row.Origin)];
            value.LatestRequestSentAt = value.LatestRequestSentAt is null
                ? row.SentAt
                : Later(value.LatestRequestSentAt.Value, row.SentAt);
            value.LastCommunicatedAt = Later(value.LastCommunicatedAt, row.SentAt);
        }

        DomainPolicy[] policies = await db.DomainPolicies
            .Where(policy => policy.RevokedAt == null && (policy.ExpiresAt == null || policy.ExpiresAt > now))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, DateTimeOffset?> circuits = await db.RemoteDomainCircuits
            .Where(circuit => accumulators.Keys.Contains(circuit.Domain))
            .ToDictionaryAsync(circuit => circuit.Domain, circuit => circuit.OpenUntil, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<RemoteInstanceView> result = accumulators.Values.Select(value =>
        {
            DomainPolicy? effectivePolicy = policies
                .Where(policy => MatchesDomain(value.Host, policy.Domain))
                .OrderByDescending(policy => policy.Domain.Length)
                .ThenByDescending(policy => policy.CreatedAt)
                .FirstOrDefault();
            bool isBlocked = effectivePolicy?.Kind == FederationPolicyKind.Reject;
            bool isSuspended = effectivePolicy?.Kind == FederationPolicyKind.PauseOutbound;
            bool isNotResponding = circuits.TryGetValue(value.Host, out DateTimeOffset? openUntil) && openUntil > now;
            return new RemoteInstanceView(
                CreateStableId(value.Host),
                value.CaughtAt,
                value.Host,
                value.UsersCount,
                value.NotesCount,
                value.FollowingCount,
                value.FollowersCount,
                value.LatestRequestSentAt,
                value.LastCommunicatedAt,
                isNotResponding,
                isSuspended,
                isBlocked,
                SoftwareName: null,
                SoftwareVersion: null,
                OpenRegistrations: null,
                Name: null,
                Description: null,
                MaintainerName: null,
                MaintainerEmail: null,
                IconUrl: null,
                FaviconUrl: null,
                ThemeColor: null,
                InfoUpdatedAt: null);
        });

        result = result.Where(value =>
            (query.Blocked is null || value.IsBlocked == query.Blocked) &&
            (query.NotResponding is null || value.IsNotResponding == query.NotResponding) &&
            (query.Suspended is null || value.IsSuspended == query.Suspended) &&
            (query.Federating is null || (value.FollowingCount > 0 || value.FollowersCount > 0) == query.Federating) &&
            (query.Subscribing is null || (value.FollowersCount > 0) == query.Subscribing) &&
            (query.Publishing is null || (value.FollowingCount > 0) == query.Publishing));

        result = Order(result, query.Sort, query.Descending);
        return result.Skip(query.Offset).Take(query.Limit).ToArray();
    }

    private static IEnumerable<RemoteInstanceView> Order(
        IEnumerable<RemoteInstanceView> source,
        RemoteInstanceSortField field,
        bool descending)
    {
        IOrderedEnumerable<RemoteInstanceView> ordered = (field, descending) switch
        {
            (RemoteInstanceSortField.Notes, true) => source.OrderByDescending(value => value.NotesCount),
            (RemoteInstanceSortField.Notes, false) => source.OrderBy(value => value.NotesCount),
            (RemoteInstanceSortField.Users, true) => source.OrderByDescending(value => value.UsersCount),
            (RemoteInstanceSortField.Users, false) => source.OrderBy(value => value.UsersCount),
            (RemoteInstanceSortField.Following, true) => source.OrderByDescending(value => value.FollowingCount),
            (RemoteInstanceSortField.Following, false) => source.OrderBy(value => value.FollowingCount),
            (RemoteInstanceSortField.Followers, true) => source.OrderByDescending(value => value.FollowersCount),
            (RemoteInstanceSortField.Followers, false) => source.OrderBy(value => value.FollowersCount),
            (RemoteInstanceSortField.LastCommunicated, true) => source.OrderByDescending(value => value.LastCommunicatedAt),
            (RemoteInstanceSortField.LastCommunicated, false) => source.OrderBy(value => value.LastCommunicatedAt),
            (_, true) => source.OrderByDescending(value => value.CaughtAt),
            _ => source.OrderBy(value => value.CaughtAt)
        };
        return ordered.ThenBy(value => value.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static RemoteInstanceAccumulator GetOrCreate(
        IDictionary<string, RemoteInstanceAccumulator> values,
        string host,
        DateTimeOffset caughtAt,
        DateTimeOffset lastCommunicatedAt)
    {
        if (!values.TryGetValue(host, out RemoteInstanceAccumulator? value))
        {
            value = new(host, caughtAt, lastCommunicatedAt);
            values.Add(host, value);
        }

        return value;
    }

    private static string HostFromOrigin(string origin) => new Uri(origin, UriKind.Absolute).IdnHost.ToLowerInvariant();

    private static bool MatchesDomain(string host, string policyDomain) =>
        string.Equals(host, policyDomain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + policyDomain, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static Guid CreateStableId(string host)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("activitypub.remote-instance/v1\0" + host));
        string value = Convert.ToHexString(digest.AsSpan(0, 16));
        return Guid.ParseExact(value, "N");
    }

    private sealed class RemoteInstanceAccumulator(
        string host,
        DateTimeOffset caughtAt,
        DateTimeOffset lastCommunicatedAt)
    {
        public string Host { get; } = host;
        public DateTimeOffset CaughtAt { get; set; } = caughtAt;
        public DateTimeOffset LastCommunicatedAt { get; set; } = lastCommunicatedAt;
        public DateTimeOffset? LatestRequestSentAt { get; set; }
        public long UsersCount { get; set; }
        public long NotesCount { get; set; }
        public long FollowingCount { get; set; }
        public long FollowersCount { get; set; }
    }
}
