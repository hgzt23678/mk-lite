using System.Globalization;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class FederationQueryStore(IDbContextFactory<FederationDbContext> contextFactory) : IFederationQueryStore
{
    public async Task<ActorDocument?> FindLocalActorByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        string normalized = username.ToUpperInvariant();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ReadActorAsync(db, x => x.NormalizedUsername == normalized && !x.IsSuspended, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActorDocument?> FindLocalActorByIriAsync(string actorIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ReadActorAsync(db, x => x.Iri == actorIri && !x.IsSuspended, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredDocument?> FindObjectAsync(string iri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Objects
            .Where(x => x.Iri == iri)
            .Select(x => new { x.Iri, x.RawJson, x.PayloadHash, x.UpdatedAt, x.Visibility, x.OwnerIri })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new(row.Iri, "application/activity+json", Encoding.UTF8.GetBytes(row.RawJson), QuoteEtag(row.PayloadHash), row.UpdatedAt, row.Visibility, row.OwnerIri);
    }

    public async Task<StoredDocument?> FindActivityAsync(string iri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Activities
            .Where(x => x.Iri == iri)
            .Select(x => new { x.Iri, x.RawJson, x.PayloadHash, x.ReceivedAt, x.Visibility, x.ActorIri })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new(row.Iri, "application/activity+json", Encoding.UTF8.GetBytes(row.RawJson), QuoteEtag(row.PayloadHash), row.ReceivedAt, row.Visibility, row.ActorIri);
    }

    public async Task<CursorPage<CollectionEntry>> ReadCollectionAsync(
        string actorIri,
        string collection,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        int limit = request.ValidatedLimit;
        CollectionCursor? cursor = DecodeCursor(request.Cursor);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        IQueryable<CollectionEntry> query = collection switch
        {
            "outbox" => ActivityCollection(db, actorIri, null, cursor),
            "inbox" => InboxCollection(db, actorIri, cursor),
            "liked" => ActivityCollection(db, actorIri, "Like", cursor),
            "followers" => FollowerCollection(db, actorIri, followers: true, cursor),
            "following" => FollowerCollection(db, actorIri, followers: false, cursor),
            "featured" => db.Objects.Where(x => x.OwnerIri == actorIri && !x.IsDeleted &&
                    (x.Visibility == Visibility.Public || x.Visibility == Visibility.Unlisted))
                .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Iri)
                .Select(x => new CollectionEntry(x.Iri, Encoding.UTF8.GetBytes(x.RawJson), x.PublishedAt)),
            _ => throw new ArgumentOutOfRangeException(nameof(collection), "Unknown collection.")
        };

        List<CollectionEntry> rows = await query.Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        string? next = rows.Count > limit ? EncodeCursor(rows[limit - 1]) : null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        DateTimeOffset lastModified = rows.Count == 0 ? DateTimeOffset.UnixEpoch : rows.Max(x => x.PublishedAt);
        return new(rows, next, lastModified);
    }

    public async Task<bool> IsAuthorizedRecipientAsync(
        string resourceIri,
        string requesterActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var resource = await db.Objects
            .Where(x => x.Iri == resourceIri)
            .Select(x => new { x.OwnerIri, x.Visibility })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (resource is null)
        {
            var activity = await db.Activities
                .Where(x => x.Iri == resourceIri)
                .Select(x => new { x.ActorIri, x.Visibility, x.Id })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (activity is null)
            {
                return false;
            }

            if (activity.Visibility is Visibility.Public or Visibility.Unlisted ||
                string.Equals(activity.ActorIri, requesterActorIri, StringComparison.Ordinal))
            {
                return true;
            }

            return await db.ActivityRecipients.AnyAsync(
                x => x.ActivityId == activity.Id && x.RecipientIri == requesterActorIri,
                cancellationToken).ConfigureAwait(false);
        }

        if (resource.Visibility is Visibility.Public or Visibility.Unlisted ||
            string.Equals(resource.OwnerIri, requesterActorIri, StringComparison.Ordinal))
        {
            return true;
        }

        if (resource.Visibility == Visibility.FollowersOnly && await db.FollowRelations.AnyAsync(
                x => x.FollowerIri == requesterActorIri && x.FollowedIri == resource.OwnerIri && x.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await (
            from activity in db.Activities
            join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
            where activity.ObjectIri == resourceIri && recipient.RecipientIri == requesterActorIri
            select recipient.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ContainsLocalRecipientAsync(
        IEnumerable<string> recipientIris,
        CancellationToken cancellationToken)
    {
        string[] iris = recipientIris.Distinct(StringComparer.Ordinal).ToArray();
        if (iris.Length == 0)
        {
            return false;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LocalActors.AnyAsync(x => !x.IsSuspended && iris.Contains(x.Iri), cancellationToken).ConfigureAwait(false);
    }

    public async Task<NodeInfoCounts> GetNodeInfoCountsAsync(CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        long users = await db.LocalActors.LongCountAsync(x => !x.IsSuspended, cancellationToken).ConfigureAwait(false);
        long posts = await (
            from federatedObject in db.Objects
            join actor in db.LocalActors on federatedObject.OwnerIri equals actor.Iri
            where !federatedObject.IsDeleted
            select federatedObject.Id).LongCountAsync(cancellationToken).ConfigureAwait(false);
        string[] remoteActorIris = await db.RemoteActors.Where(x => x.GoneAt == null)
            .Select(x => x.Iri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        long remoteDomains = remoteActorIris.Select(iri => new Uri(iri).IdnHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .LongCount();
        long localMediaBytes = await db.Media.Where(x =>
                x.State == MediaState.Available && db.LocalActors.Any(actor => actor.Iri == x.OwnerActorIri))
            .SumAsync(x => (long?)x.Length, cancellationToken).ConfigureAwait(false) ?? 0;
        long remoteMediaBytes = await db.Media.Where(x =>
                x.State == MediaState.Available && !db.LocalActors.Any(actor => actor.Iri == x.OwnerActorIri))
            .SumAsync(x => (long?)x.Length, cancellationToken).ConfigureAwait(false) ?? 0;
        return new(users, posts, remoteDomains, localMediaBytes, remoteMediaBytes);
    }

    private static async Task<ActorDocument?> ReadActorAsync(
        FederationDbContext db,
        System.Linq.Expressions.Expression<Func<LocalActor, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var row = await (
            from actor in db.LocalActors.Where(predicate)
            join key in db.ActorKeys on actor.ActiveKeyId equals key.Id
            where key.State == ActorKeyState.Active
            select new
            {
                actor.Id,
                actor.Iri,
                actor.Username,
                actor.Kind,
                actor.DisplayName,
                actor.SummaryHtml,
                actor.ManuallyApprovesFollowers,
                actor.Discoverable,
                actor.Indexable,
                key.KeyIri,
                key.PublicKeyPem,
                actor.UpdatedAt
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ActorPublicKeyDocument> retiredKeys = await db.ActorKeys
            .Where(x => x.OwnerIri == row.Iri && x.State == ActorKeyState.Retired && x.ExpiresAt > now)
            .OrderByDescending(x => x.RetiredAt)
            .Select(x => new ActorPublicKeyDocument(x.KeyIri, x.PublicKeyPem, x.ExpiresAt!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ActorDocument(
            row.Id,
            row.Iri,
            row.Username,
            row.Kind,
            row.DisplayName,
            row.SummaryHtml,
            row.ManuallyApprovesFollowers,
            row.Discoverable,
            row.Indexable,
            row.KeyIri,
            row.PublicKeyPem,
            row.UpdatedAt,
            retiredKeys);
    }

    private static IQueryable<CollectionEntry> ActivityCollection(
        FederationDbContext db,
        string actorIri,
        string? activityType,
        CollectionCursor? cursor)
    {
        IQueryable<ActivityRecord> query = db.Activities.Where(x =>
            x.ActorIri == actorIri && x.Direction != ActivityDirection.Inbound &&
            (x.Visibility == Visibility.Public || x.Visibility == Visibility.Unlisted));
        if (activityType is not null)
        {
            query = query.Where(x => x.Type == activityType);
        }

        if (cursor is not null)
        {
            query = query.Where(x => x.OccurredAt < cursor.PublishedAt ||
                (x.OccurredAt == cursor.PublishedAt && string.Compare(x.Iri, cursor.Iri, StringComparison.Ordinal) < 0));
        }

        return query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Iri)
            .Select(x => new CollectionEntry(x.Iri, Encoding.UTF8.GetBytes(x.RawJson), x.OccurredAt));
    }

    private static IQueryable<CollectionEntry> FollowerCollection(
        FederationDbContext db,
        string actorIri,
        bool followers,
        CollectionCursor? cursor)
    {
        IQueryable<FollowRelation> query = db.FollowRelations.Where(x =>
            x.State == FollowState.Accepted && (followers ? x.FollowedIri == actorIri : x.FollowerIri == actorIri));
        if (cursor is not null)
        {
            query = query.Where(x => x.UpdatedAt < cursor.PublishedAt);
        }

        return query.OrderByDescending(x => x.UpdatedAt)
            .Select(x => new CollectionEntry(
                followers ? x.FollowerIri : x.FollowedIri,
                JsonSerializer.SerializeToUtf8Bytes(followers ? x.FollowerIri : x.FollowedIri),
                x.UpdatedAt));
    }

    private static IQueryable<CollectionEntry> InboxCollection(
        FederationDbContext db,
        string actorIri,
        CollectionCursor? cursor)
    {
        IQueryable<ActivityRecord> query =
            from activity in db.Activities
            join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
            where activity.Direction == ActivityDirection.Inbound && recipient.RecipientIri == actorIri
            select activity;
        if (cursor is not null)
        {
            query = query.Where(x => x.OccurredAt < cursor.PublishedAt ||
                (x.OccurredAt == cursor.PublishedAt && string.Compare(x.Iri, cursor.Iri, StringComparison.Ordinal) < 0));
        }

        return query.Distinct().OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Iri)
            .Select(x => new CollectionEntry(x.Iri, Encoding.UTF8.GetBytes(x.RawJson), x.OccurredAt));
    }

    private static string QuoteEtag(string payloadHash) => $"\"sha256-{payloadHash}\"";

    private static string EncodeCursor(CollectionEntry entry)
    {
        string value = string.Create(CultureInfo.InvariantCulture, $"{entry.PublishedAt.UtcTicks}|{entry.Iri}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static CollectionCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > 4_096)
        {
            throw new ArgumentException("Cursor is too long.", nameof(value));
        }

        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            int delimiter = decoded.IndexOf('|', StringComparison.Ordinal);
            if (delimiter <= 0 || !long.TryParse(decoded.AsSpan(0, delimiter), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            {
                throw new FormatException();
            }

            string iri = decoded[(delimiter + 1)..];
            return new(new DateTimeOffset(ticks, TimeSpan.Zero), CanonicalIri.RequireAbsoluteHttp(iri, nameof(value)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Cursor is malformed.", nameof(value), exception);
        }
    }

    private sealed record CollectionCursor(DateTimeOffset PublishedAt, string Iri);
}
