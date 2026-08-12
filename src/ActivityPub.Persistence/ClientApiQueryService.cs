using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class ClientApiQueryService(IDbContextFactory<FederationDbContext> contextFactory) : IClientApiQueryService
{
    private static readonly IReadOnlyList<ClientCustomEmojiView> EmptyEmojis = [];
    private static readonly IReadOnlyList<ClientProfileFieldView> EmptyFields = [];

    public async Task<ClientReactionSummaryView> ReadPostReactionsAsync(
        Guid postId,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? objectIri = await db.Objects
            .Where(item => item.Id == postId && !item.IsDeleted)
            .Select(item => item.Iri)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (objectIri is null)
        {
            return new ClientReactionSummaryView(
                new Dictionary<string, long>(StringComparer.Ordinal),
                null,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        LikeRelation[] likes = await db.LikeRelations
            .Where(item => item.ObjectIri == objectIri && item.State == FederatedRelationState.Active)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        EmojiReactionRelation[] emojis = await db.EmojiReactionRelations
            .Where(item => item.ObjectIri == objectIri && item.State == FederatedRelationState.Active)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, long> reactions = likes.Select(item => item.EffectiveReaction)
            .Concat(emojis.Select(item => item.Reaction))
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);
        string? viewerReaction = viewerActorIri is null
            ? null
            : likes.FirstOrDefault(item => string.Equals(item.ActorIri, viewerActorIri, StringComparison.Ordinal))?.EffectiveReaction;
        viewerReaction ??= viewerActorIri is null
            ? null
            : emojis.FirstOrDefault(item => string.Equals(item.ActorIri, viewerActorIri, StringComparison.Ordinal))?.Reaction;
        var customEmojiUrls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string reaction, string url) in likes
                     .Where(item => item.CustomEmojiUrl is not null)
                     .Select(item => (item.EffectiveReaction, item.CustomEmojiUrl!))
                     .Concat(emojis
                         .Where(item => item.CustomEmojiUrl is not null)
                         .Select(item => (item.Reaction, item.CustomEmojiUrl!))))
        {
            string token = PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(url))[..32];
            customEmojiUrls.TryAdd(reaction.Trim(':'), $"/media/proxy/{postId:N}/{token}");
        }

        return new ClientReactionSummaryView(reactions, viewerReaction, customEmojiUrls);
    }

    public async Task<IReadOnlyList<ClientReactionActorView>> ReadPostReactionActorsAsync(
        Guid postId,
        string reaction,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reaction);
        if (reaction.Length > 512 || reaction.Any(char.IsControl))
        {
            throw new ArgumentException("The reaction is invalid.", nameof(reaction));
        }

        int safeLimit = Math.Clamp(limit, 1, 100);
        ClientPostView? visiblePost = await FindPostAsync(
            postId,
            viewerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (visiblePost is null)
        {
            return [];
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<LikeRelation> likeQuery = db.LikeRelations.Where(item =>
            item.ObjectIri == visiblePost.Iri &&
            item.State == FederatedRelationState.Active);
        likeQuery = string.Equals(reaction, FederatedReaction.DefaultValue, StringComparison.Ordinal)
            ? likeQuery.Where(item => item.Reaction == null || item.Reaction == FederatedReaction.DefaultValue)
            : likeQuery.Where(item => item.Reaction == reaction);

        var likes = await likeQuery
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(safeLimit)
            .Select(item => new ClientReactionActorView(item.Id, item.CreatedAt, item.ActorIri))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var emojiReactions = await db.EmojiReactionRelations
            .Where(item => item.ObjectIri == visiblePost.Iri &&
                item.State == FederatedRelationState.Active &&
                item.Reaction == reaction)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(safeLimit)
            .Select(item => new ClientReactionActorView(item.Id, item.CreatedAt, item.ActorIri))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return likes.Concat(emojiReactions)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.RelationId)
            .Take(safeLimit)
            .ToArray();
    }

    public async Task<IReadOnlyList<ClientAnnounceActorView>> ReadPostAnnounceActorsAsync(
        Guid postId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 100);
        ClientPostView? visiblePost = await FindPostAsync(
            postId,
            viewerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (visiblePost is null)
        {
            return [];
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AnnounceRelations
            .Where(item => item.ObjectIri == visiblePost.Iri && item.State == FederatedRelationState.Active)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(safeLimit)
            .Select(item => new ClientAnnounceActorView(item.Id, item.CreatedAt, item.ActorIri))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientAccountView?> FindAccountByLookupAsync(
        string account,
        string localDomain,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        string normalized = account.Trim().TrimStart('@');
        string[] parts = normalized.Split('@', StringSplitOptions.RemoveEmptyEntries);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (parts.Length == 1 || parts.Length == 2 && string.Equals(parts[1], localDomain, StringComparison.OrdinalIgnoreCase))
        {
            string username = parts[0].ToUpperInvariant();
            LocalActor? actor = await db.LocalActors.SingleOrDefaultAsync(
                x => x.NormalizedUsername == username && !x.IsSuspended,
                cancellationToken).ConfigureAwait(false);
            return actor is null ? null : await MapLocalAccountAsync(db, actor, localDomain, cancellationToken).ConfigureAwait(false);
        }

        if (parts.Length != 2)
        {
            return null;
        }

        string remoteUsername = parts[0];
        string remoteDomain = parts[1];
        RemoteActor[] remotes = await db.RemoteActors
            .Where(x => x.PreferredUsername == remoteUsername && x.GoneAt == null)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        RemoteActor? remote = remotes.FirstOrDefault(x =>
            string.Equals(new Uri(x.Iri).IdnHost, remoteDomain, StringComparison.OrdinalIgnoreCase));
        return remote is null ? null : await MapRemoteAccountAsync(db, remote, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClientAccountView>> SearchAccountsByUsernameAsync(
        string username,
        string? host,
        int limit,
        string localDomain,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string prefix = username.Trim().TrimStart('@').ToUpperInvariant();
        if (prefix.Length == 0)
        {
            return [];
        }

        var results = new List<ClientAccountView>();
        LocalActor[] locals = await db.LocalActors
            .Where(actor => actor.NormalizedUsername.StartsWith(prefix) && !actor.IsSuspended)
            .OrderBy(actor => actor.NormalizedUsername)
            .Take(limit)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (LocalActor actor in locals)
        {
            results.Add(await MapLocalAccountAsync(db, actor, localDomain, cancellationToken).ConfigureAwait(false));
        }

        if (results.Count < limit)
        {
            string remotePrefix = username.Trim().TrimStart('@');
            IQueryable<RemoteActor> remotes = db.RemoteActors
                .Where(actor => actor.GoneAt == null &&
                                actor.PreferredUsername != null &&
                                actor.PreferredUsername.StartsWith(remotePrefix));
            if (!string.IsNullOrWhiteSpace(host))
            {
                string domain = host.Trim().ToLowerInvariant();
                remotes = remotes.Where(actor => actor.Iri.StartsWith("https://" + domain + "/", StringComparison.OrdinalIgnoreCase) ||
                                                actor.Iri.StartsWith("http://" + domain + "/", StringComparison.OrdinalIgnoreCase));
            }

            RemoteActor[] remoteMatches = await remotes
                .OrderBy(actor => actor.PreferredUsername!)
                .Take(limit - results.Count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (RemoteActor actor in remoteMatches)
            {
                results.Add(await MapRemoteAccountAsync(db, actor, cancellationToken).ConfigureAwait(false));
            }
        }

        return results;
    }

    public async Task<string?> FindLocalActorIriAsync(string username, CancellationToken cancellationToken)
    {
        string normalized = username.ToUpperInvariant();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LocalActors.Where(x => x.NormalizedUsername == normalized && !x.IsSuspended)
            .Select(x => x.Iri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientAccountView?> FindAccountByIdAsync(
        Guid id,
        string localDomain,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LocalActor? local = await db.LocalActors.SingleOrDefaultAsync(x => x.Id == id && !x.IsSuspended, cancellationToken).ConfigureAwait(false);
        if (local is not null)
        {
            return await MapLocalAccountAsync(db, local, localDomain, cancellationToken).ConfigureAwait(false);
        }

        RemoteActor? remote = await db.RemoteActors.SingleOrDefaultAsync(x => x.Id == id && x.GoneAt == null, cancellationToken).ConfigureAwait(false);
        return remote is null ? null : await MapRemoteAccountAsync(db, remote, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView?> FindRelationshipAsync(
        string ownerActorIri,
        Guid accountId,
        string localDomain,
        CancellationToken cancellationToken)
    {
        ClientAccountView? target = await FindAccountByIdAsync(accountId, localDomain, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FollowState? outboundFollow = await db.FollowRelations
            .Where(x => x.FollowerIri == ownerActorIri && x.FollowedIri == target.Iri)
            .Select(x => (FollowState?)x.State)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        FollowState? inboundFollow = await db.FollowRelations
            .Where(x => x.FollowerIri == target.Iri && x.FollowedIri == ownerActorIri)
            .Select(x => (FollowState?)x.State)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        UserMute? mute = await db.UserMutes.SingleOrDefaultAsync(x =>
            x.OwnerActorIri == ownerActorIri && x.TargetActorIri == target.Iri && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > now),
            cancellationToken).ConfigureAwait(false);
        bool blocking = await db.UserBlocks.AnyAsync(x =>
            x.OwnerActorIri == ownerActorIri && x.TargetActorIri == target.Iri &&
            x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        bool blockedBy = await db.UserBlocks.AnyAsync(x =>
            x.OwnerActorIri == target.Iri && x.TargetActorIri == ownerActorIri &&
            x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        return new(
            target.Id,
            outboundFollow == FollowState.Accepted,
            true,
            false,
            inboundFollow == FollowState.Accepted,
            blocking,
            blockedBy,
            mute is not null,
            mute?.HideNotifications ?? false,
            outboundFollow == FollowState.Pending,
            inboundFollow == FollowState.Pending,
            false,
            false,
            string.Empty);
    }

    public async Task<ClientPage<ClientFollowRelationView>> ReadFollowRelationsAsync(
        Guid accountId,
        bool followers,
        Guid? beforeId,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 100);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        FollowRelation? before = beforeId is null
            ? null
            : await db.FollowRelations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == beforeId.Value, cancellationToken)
                .ConfigureAwait(false);
        FollowRelation? after = afterId is null
            ? null
            : await db.FollowRelations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == afterId.Value, cancellationToken)
                .ConfigureAwait(false);
        if (beforeId is not null && before is null || afterId is not null && after is null)
        {
            return new([], null, null);
        }

        string? actorIri = await db.LocalActors.AsNoTracking()
            .Where(value => value.Id == accountId && !value.IsSuspended)
            .Select(value => value.Iri)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        actorIri ??= await db.RemoteActors.AsNoTracking()
            .Where(value => value.Id == accountId && value.GoneAt == null)
            .Select(value => value.Iri)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (actorIri is null)
        {
            return new([], null, null);
        }

        IQueryable<FollowRelation> query = db.FollowRelations.AsNoTracking()
            .Where(value => value.State == FollowState.Accepted &&
                (followers ? value.FollowedIri == actorIri : value.FollowerIri == actorIri));
        if (before is not null)
        {
            query = query.Where(value => value.UpdatedAt < before.UpdatedAt ||
                value.UpdatedAt == before.UpdatedAt && value.Id.CompareTo(before.Id) < 0);
        }
        if (after is not null)
        {
            query = query.Where(value => value.UpdatedAt > after.UpdatedAt ||
                value.UpdatedAt == after.UpdatedAt && value.Id.CompareTo(after.Id) > 0);
        }

        bool ascending = after is not null && before is null;
        IQueryable<FollowRelation> ordered = ascending
            ? query.OrderBy(value => value.UpdatedAt).ThenBy(value => value.Id)
            : query.OrderByDescending(value => value.UpdatedAt).ThenByDescending(value => value.Id);
        FollowRelation[] relations = await ordered
            .Take(safeLimit + 1)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        bool hasMore = relations.Length > safeLimit;
        FollowRelation[] page = relations.Take(safeLimit).ToArray();
        var result = new List<ClientFollowRelationView>(page.Length);
        foreach (FollowRelation relation in page)
        {
            ClientAccountView? follower = await FindAccountByIriCoreAsync(
                db,
                relation.FollowerIri,
                cancellationToken).ConfigureAwait(false);
            ClientAccountView? followee = await FindAccountByIriCoreAsync(
                db,
                relation.FollowedIri,
                cancellationToken).ConfigureAwait(false);
            if (follower is null || followee is null)
            {
                continue;
            }

            result.Add(new(relation.Id, relation.CreatedAt, follower, followee));
        }

        ClientPageCursor? next = hasMore && page.Length > 0
            ? new(page[^1].Id, page[^1].UpdatedAt)
            : null;
        return new(result, next, null);
    }

    public async Task<ClientPostView?> FindPostAsync(
        Guid id,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject? item = await db.Objects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (item is null || !await CanViewAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await MapPostAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientPostView?> FindStreamPostAsync(
        Guid id,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject? item = await db.Objects.SingleOrDefaultAsync(
            x => x.Id == id && !x.IsDeleted,
            cancellationToken).ConfigureAwait(false);
        if (item is null || !await CanViewAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (localOnly && !await db.LocalActors.AnyAsync(x => x.Iri == item.OwnerIri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        bool hiddenByAdministration = await db.ActorPolicies.AnyAsync(x =>
            x.ActorIri == item.OwnerIri && x.Kind == ModerationActionKind.MuteActor && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false);
        DomainPolicy[] silenced = await db.DomainPolicies.Where(x =>
            x.Kind == FederationPolicyKind.Silence && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > now)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (hiddenByAdministration || MatchesDomainPolicy(item.OwnerIri, silenced))
        {
            return null;
        }

        if (viewerActorIri is not null && await db.UserMutes.AnyAsync(x =>
                x.OwnerActorIri == viewerActorIri && x.TargetActorIri == item.OwnerIri && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (audience == ClientStreamAudience.Public)
        {
            return item.Visibility == Visibility.Public
                ? await MapPostAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false)
                : null;
        }

        if (viewerActorIri is null)
        {
            return null;
        }

        bool inHome = string.Equals(item.OwnerIri, viewerActorIri, StringComparison.Ordinal) ||
            await db.FollowRelations.AnyAsync(x =>
                x.FollowerIri == viewerActorIri && x.FollowedIri == item.OwnerIri && x.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false);
        return inHome
            ? await MapPostAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<bool> CanReceiveStreamEventAsync(
        StreamEvent streamEvent,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);
        if (localOnly && !streamEvent.IsLocal)
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (streamEvent.Kind == StreamEventKind.PollVoted && streamEvent.ResourceId is { } pollResourceId)
        {
            FederatedObject? question = await db.Objects.SingleOrDefaultAsync(
                value => value.Id == pollResourceId && !value.IsDeleted,
                cancellationToken).ConfigureAwait(false);
            if (question is null || localOnly && !await db.LocalActors.AnyAsync(
                    value => value.Iri == question.OwnerIri,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            return await CanViewAsync(db, question, viewerActorIri, cancellationToken).ConfigureAwait(false);
        }

        if (await db.ActorPolicies.AnyAsync(x =>
                x.ActorIri == streamEvent.ActorIri && x.Kind == ModerationActionKind.MuteActor && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        DomainPolicy[] silenced = await db.DomainPolicies.Where(x =>
            x.Kind == FederationPolicyKind.Silence && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > now)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (MatchesDomainPolicy(streamEvent.ActorIri, silenced))
        {
            return false;
        }

        if (viewerActorIri is not null && await db.UserMutes.AnyAsync(x =>
                x.OwnerActorIri == viewerActorIri && x.TargetActorIri == streamEvent.ActorIri && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (audience == ClientStreamAudience.Public)
        {
            return streamEvent.Visibility == Visibility.Public;
        }

        if (viewerActorIri is null)
        {
            return false;
        }

        bool ownerOrFollowed = string.Equals(streamEvent.ActorIri, viewerActorIri, StringComparison.Ordinal) ||
            await db.FollowRelations.AnyAsync(x =>
                x.FollowerIri == viewerActorIri && x.FollowedIri == streamEvent.ActorIri && x.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false);
        if (!ownerOrFollowed)
        {
            return false;
        }

        if (streamEvent.Visibility is Visibility.Public or Visibility.Unlisted)
        {
            return true;
        }

        if (streamEvent.Visibility == Visibility.FollowersOnly)
        {
            return ownerOrFollowed;
        }

        return streamEvent.ResourceIri is not null && await (from activity in db.Activities
                                                             join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
                                                             where activity.ObjectIri == streamEvent.ResourceIri && recipient.RecipientIri == viewerActorIri
                                                             select recipient.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientAccountView?> FindAccountByIriAsync(
        string actorIri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorIri);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await FindAccountByIriCoreAsync(db, actorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientPage<ClientPostView>> ReadPublicTimelineAsync(
        Guid? beforeId,
        int limit,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        int safeLimit = ValidateLimit(limit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? before = beforeId is null
            ? null
            : await db.Objects.Where(x => x.Id == beforeId).Select(x => (DateTimeOffset?)x.PublishedAt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> localActors = localOnly
            ? (await db.LocalActors.Select(x => x.Iri).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal)
            : [];
        DomainPolicy[] silenced = await db.DomainPolicies
            .Where(x => x.Kind == FederationPolicyKind.Silence && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] administrativelyMuted = await db.ActorPolicies
            .Where(x => x.Kind == ModerationActionKind.MuteActor && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow))
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        // An unlisted object is addressable to its recipients and belongs in a
        // follower's home timeline, but it must never be projected into a
        // public/local timeline.  Keeping this predicate at the persistence
        // boundary prevents every HTTP and UI adapter from having to remember
        // to filter it independently.
        IQueryable<FederatedObject> query = db.Objects.Where(x =>
            !x.IsDeleted && x.Visibility == Visibility.Public);
        if (before is not null)
        {
            query = query.Where(x => x.PublishedAt < before.Value);
        }

        List<FederatedObject> candidates = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(safeLimit * 4 + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        candidates = candidates.Where(x =>
                (!localOnly || localActors.Contains(x.OwnerIri)) &&
                !MatchesDomainPolicy(x.OwnerIri, silenced) &&
                !administrativelyMuted.Contains(x.OwnerIri, StringComparer.Ordinal))
            .Take(safeLimit + 1)
            .ToList();
        List<ClientPostView> statuses = [];
        foreach (FederatedObject item in candidates.Take(safeLimit))
        {
            statuses.Add(await MapPostAsync(db, item, null, cancellationToken).ConfigureAwait(false));
        }

        return new(
            statuses,
            candidates.Count > safeLimit ? new(candidates[safeLimit - 1].Id, candidates[safeLimit - 1].PublishedAt) : null,
            statuses.FirstOrDefault() is { } first ? new(first.Id, first.CreatedAt) : null);
    }

    public async Task<ClientPage<ClientPostView>> ReadAccountPostsAsync(
        Guid accountId,
        string localDomain,
        Guid? beforeId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ClientAccountView? account = await FindAccountByIdAsync(accountId, localDomain, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return new([], null, null);
        }

        int safeLimit = ValidateLimit(limit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? before = beforeId is null
            ? null
            : await db.Objects.Where(x => x.Id == beforeId).Select(x => (DateTimeOffset?)x.PublishedAt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<FederatedObject> query = db.Objects.Where(x => x.OwnerIri == account.Iri && !x.IsDeleted);
        if (before is not null)
        {
            query = query.Where(x => x.PublishedAt < before.Value);
        }

        List<FederatedObject> candidates = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(safeLimit * 2 + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ClientPostView> statuses = [];
        foreach (FederatedObject item in candidates)
        {
            if (await CanViewAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false))
            {
                statuses.Add(await MapPostAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false));
                if (statuses.Count == safeLimit + 1)
                {
                    break;
                }
            }
        }

        ClientPageCursor? next = statuses.Count > safeLimit
            ? new(statuses[safeLimit - 1].Id, statuses[safeLimit - 1].CreatedAt)
            : null;
        if (statuses.Count > safeLimit)
        {
            statuses.RemoveAt(statuses.Count - 1);
        }

        return new(statuses, next, statuses.FirstOrDefault() is { } first ? new(first.Id, first.CreatedAt) : null);
    }

    public async Task<ClientPage<ClientPostView>> ReadHomeTimelineAsync(
        string viewerActorIri,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = ValidateLimit(limit);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string[] followed = await db.FollowRelations
            .Where(x => x.FollowerIri == viewerActorIri && x.State == FollowState.Accepted)
            .Select(x => x.FollowedIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] muted = await db.UserMutes
            .Where(x => x.OwnerActorIri == viewerActorIri && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => x.TargetActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] administrativelyMuted = await db.ActorPolicies
            .Where(x => x.Kind == ModerationActionKind.MuteActor && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] blocked = await db.UserBlocks.Where(x =>
                (x.OwnerActorIri == viewerActorIri || x.TargetActorIri == viewerActorIri) &&
                x.State == FederatedRelationState.Active)
            .Select(x => x.OwnerActorIri == viewerActorIri ? x.TargetActorIri : x.OwnerActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] owners = followed.Append(viewerActorIri)
            .Except(muted, StringComparer.Ordinal)
            .Except(administrativelyMuted, StringComparer.Ordinal)
            .Except(blocked, StringComparer.Ordinal)
            .ToArray();
        DateTimeOffset? before = beforeId is null
            ? null
            : await db.Objects.Where(x => x.Id == beforeId).Select(x => (DateTimeOffset?)x.PublishedAt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<FederatedObject> query = db.Objects.Where(x => owners.Contains(x.OwnerIri) && !x.IsDeleted);
        if (before is not null)
        {
            query = query.Where(x => x.PublishedAt < before.Value);
        }

        List<FederatedObject> candidates = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(safeLimit * 3 + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ClientPostView> statuses = [];
        foreach (FederatedObject item in candidates)
        {
            if (await CanViewAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false))
            {
                statuses.Add(await MapPostAsync(db, item, viewerActorIri, cancellationToken).ConfigureAwait(false));
                if (statuses.Count == safeLimit + 1)
                {
                    break;
                }
            }
        }

        ClientPageCursor? next = statuses.Count > safeLimit
            ? new(statuses[safeLimit - 1].Id, statuses[safeLimit - 1].CreatedAt)
            : null;
        if (statuses.Count > safeLimit)
        {
            statuses.RemoveAt(statuses.Count - 1);
        }

        return new(statuses, next, statuses.FirstOrDefault() is { } first ? new(first.Id, first.CreatedAt) : null);
    }

    private static int ValidateLimit(int limit) => limit is >= 1 and <= 40
        ? limit
        : throw new ArgumentOutOfRangeException(nameof(limit), "Mastodon page limit must be between 1 and 40.");

    private static bool MatchesDomainPolicy(string actorIri, IEnumerable<DomainPolicy> policies)
    {
        string domain = new Uri(actorIri).IdnHost;
        return policies.Any(policy => string.Equals(domain, policy.Domain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith('.' + policy.Domain, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> CanViewAsync(
        FederationDbContext db,
        FederatedObject item,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        if (viewerActorIri is null)
        {
            return item.Visibility is Visibility.Public or Visibility.Unlisted;
        }

        if (string.Equals(viewerActorIri, item.OwnerIri, StringComparison.Ordinal))
        {
            return true;
        }

        if (await db.UserBlocks.AnyAsync(x =>
                (x.OwnerActorIri == viewerActorIri && x.TargetActorIri == item.OwnerIri ||
                 x.OwnerActorIri == item.OwnerIri && x.TargetActorIri == viewerActorIri) &&
                x.State == FederatedRelationState.Active,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (item.Visibility is Visibility.Public or Visibility.Unlisted)
        {
            return true;
        }

        if (item.Visibility == Visibility.FollowersOnly && await db.FollowRelations.AnyAsync(x =>
                x.FollowerIri == viewerActorIri && x.FollowedIri == item.OwnerIri && x.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await (from activity in db.Activities
                      join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
                      where activity.ObjectIri == item.Iri && recipient.RecipientIri == viewerActorIri
                      select recipient.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ClientPostView> MapPostAsync(
        FederationDbContext db,
        FederatedObject item,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ClientAccountView account = await FindAccountByIriCoreAsync(db, item.OwnerIri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Stored object owner has no actor projection.");
        using JsonDocument document = JsonDocument.Parse(item.RawJson);
        JsonElement root = document.RootElement;
        string content = ReadString(root, "content") ?? string.Empty;
        string summary = ReadString(root, "summary") ?? string.Empty;
        bool sensitive = root.TryGetProperty("sensitive", out JsonElement sensitiveValue) && sensitiveValue.ValueKind == JsonValueKind.True;
        string? language = ReadString(root, "language");
        (string? sourceText, string? sourceFormat) = ReadSource(root);
        bool rejectMedia = await HasRejectMediaPolicyAsync(db, item.OwnerIri, cancellationToken).ConfigureAwait(false);
        bool localOwner = await db.LocalActors.AnyAsync(x => x.Iri == item.OwnerIri, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ClientMediaView> attachments = rejectMedia ? [] : ReadAttachments(root, item.Id, item.PublishedAt, !localOwner);
        long favourites = await db.LikeRelations.LongCountAsync(
            x => x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        long reblogs = await db.AnnounceRelations.LongCountAsync(
            x => x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        bool favourited = viewerActorIri is not null && await db.LikeRelations.AnyAsync(x =>
            x.ActorIri == viewerActorIri && x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        bool reblogged = viewerActorIri is not null && await db.AnnounceRelations.AnyAsync(x =>
            x.ActorIri == viewerActorIri && x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        bool muted = viewerActorIri is not null && await db.UserMutes.AnyAsync(x =>
            x.OwnerActorIri == viewerActorIri && x.TargetActorIri == item.OwnerIri && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        muted = muted || await db.ActorPolicies.AnyAsync(x =>
            x.ActorIri == item.OwnerIri && x.Kind == ModerationActionKind.MuteActor && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        bool localOnly = ReadBoolean(root, "localOnly") || ReadBoolean(root, "_misskey_local_only");
        IReadOnlyList<ClientAccountView> visibleRecipients = item.Visibility == Visibility.MentionedOnly
            ? await ReadVisibleRecipientsAsync(db, item.Iri, item.OwnerIri, cancellationToken).ConfigureAwait(false)
            : [];
        ClientPollView? poll = item.Type == "Question"
            ? await MapPollAsync(db, item, root, viewerActorIri, cancellationToken).ConfigureAwait(false)
            : null;
        return new ClientPostView(
            Id: item.Id,
            CreatedAt: item.PublishedAt,
            InReplyToId: null,
            InReplyToAccountId: null,
            Sensitive: sensitive,
            ContentWarning: summary,
            Visibility: item.Visibility,
            Language: language,
            Iri: item.Iri,
            Url: ReadString(root, "url") ?? item.Iri,
            RepliesCount: 0,
            AnnouncesCount: reblogs,
            LikesCount: favourites,
            LikedByViewer: favourited,
            AnnouncedByViewer: reblogged,
            MutedForViewer: muted,
            BookmarkedByViewer: false,
            PinnedForViewer: false,
            SanitizedHtml: content,
            SourceText: sourceText,
            SourceFormat: sourceFormat,
            AnnouncedPost: null,
            Account: account,
            Attachments: attachments,
            Mentions: [],
            Hashtags: [],
            Emojis: [],
            Poll: poll,
            LocalOnly: localOnly,
            VisibleRecipients: visibleRecipients);
    }

    private static async Task<ClientPollView?> MapPollAsync(
        FederationDbContext db,
        FederatedObject question,
        JsonElement root,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        QuestionPoll? persisted = await db.QuestionPolls.SingleOrDefaultAsync(
            value => value.QuestionObjectId == question.Id,
            cancellationToken).ConfigureAwait(false);
        if (persisted is not null)
        {
            PollOption[] options = await db.PollOptions
                .Where(value => value.PollId == persisted.Id)
                .OrderBy(value => value.ChoiceIndex)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<int, long> counts = await db.PollVotes
                .Where(value => value.PollId == persisted.Id)
                .GroupBy(value => value.ChoiceIndex)
                .Select(group => new { Choice = group.Key, Count = group.LongCount() })
                .ToDictionaryAsync(value => value.Choice, value => value.Count, cancellationToken)
                .ConfigureAwait(false);
            int[] ownVotes = viewerActorIri is null
                ? []
                : await db.PollVotes
                    .Where(value => value.PollId == persisted.Id && value.VoterActorIri == viewerActorIri)
                    .OrderBy(value => value.ChoiceIndex)
                    .Select(value => value.ChoiceIndex)
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            ClientPollOptionView[] projected = options.Select(option => new ClientPollOptionView(
                option.Title,
                checked(option.BaselineVotesCount + counts.GetValueOrDefault(option.ChoiceIndex)))).ToArray();
            long localVoters = await db.PollVotes
                .Where(value => value.PollId == persisted.Id)
                .Select(value => value.VoterActorIri)
                .Distinct()
                .LongCountAsync(cancellationToken).ConfigureAwait(false);
            return new(
                persisted.Id,
                persisted.ExpiresAt,
                persisted.IsExpired(DateTimeOffset.UtcNow),
                persisted.Multiple,
                projected.Sum(value => value.VotesCount),
                checked(persisted.BaselineVotersCount + localVoters),
                viewerActorIri is null ? null : ownVotes.Length > 0,
                ownVotes,
                projected);
        }

        bool multiple = root.TryGetProperty("anyOf", out JsonElement anyOf);
        JsonElement choices = multiple
            ? anyOf
            : root.TryGetProperty("oneOf", out JsonElement oneOf) ? oneOf : default;
        if (choices.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            return null;
        }

        IEnumerable<JsonElement> values = choices.ValueKind == JsonValueKind.Array
            ? choices.EnumerateArray()
            : [choices];
        ClientPollOptionView[] parsed = values
            .Where(value => value.ValueKind == JsonValueKind.Object && ReadString(value, "name") is not null)
            .Select(value => new ClientPollOptionView(
                ReadString(value, "name")!,
                value.TryGetProperty("replies", out JsonElement replies) &&
                replies.ValueKind == JsonValueKind.Object &&
                replies.TryGetProperty("totalItems", out JsonElement totalItems) &&
                totalItems.TryGetInt64(out long count) ? Math.Max(0, count) : 0))
            .Take(10)
            .ToArray();
        if (parsed.Length < 2)
        {
            return null;
        }

        DateTimeOffset? expiresAt = ReadString(root, "endTime") is { } rawEndTime &&
            DateTimeOffset.TryParse(
                rawEndTime,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsedEndTime)
            ? parsedEndTime
            : null;
        long votersCount = root.TryGetProperty("votersCount", out JsonElement voters) && voters.TryGetInt64(out long parsedVoters)
            ? Math.Max(0, parsedVoters)
            : 0;
        return new(
            question.Id,
            expiresAt,
            expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow,
            multiple,
            parsed.Sum(value => value.VotesCount),
            votersCount,
            viewerActorIri is null ? null : false,
            [],
            parsed);
    }

    private static async Task<IReadOnlyList<ClientAccountView>> ReadVisibleRecipientsAsync(
        FederationDbContext db,
        string objectIri,
        string ownerIri,
        CancellationToken cancellationToken)
    {
        var recipients = await (from activity in db.Activities
                                join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
                                where activity.ObjectIri == objectIri &&
                                      activity.Direction == ActivityDirection.Outbound &&
                                      activity.Type == "Create" &&
                                      activity.ActorIri == ownerIri &&
                                      (recipient.Field == AudienceField.To || recipient.Field == AudienceField.Cc)
                                select new { activity.OccurredAt, recipient.Id, recipient.RecipientIri })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        string[] recipientIris = recipients
            .OrderBy(recipient => recipient.OccurredAt)
            .ThenBy(recipient => recipient.Id)
            .Select(recipient => recipient.RecipientIri)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new List<ClientAccountView>(recipientIris.Length);
        foreach (string recipientIri in recipientIris)
        {
            ClientAccountView? account = await FindAccountByIriCoreAsync(db, recipientIri, cancellationToken)
                .ConfigureAwait(false);
            if (account is not null)
            {
                result.Add(account);
            }
        }

        return result;
    }

    private static async Task<ClientAccountView?> FindAccountByIriCoreAsync(
        FederationDbContext db,
        string actorIri,
        CancellationToken cancellationToken)
    {
        LocalActor? local = await db.LocalActors.SingleOrDefaultAsync(x => x.Iri == actorIri && !x.IsSuspended, cancellationToken).ConfigureAwait(false);
        if (local is not null)
        {
            return await MapLocalAccountAsync(db, local, new Uri(local.Iri).IdnHost, cancellationToken).ConfigureAwait(false);
        }

        RemoteActor? remote = await db.RemoteActors.SingleOrDefaultAsync(x => x.Iri == actorIri && x.GoneAt == null, cancellationToken).ConfigureAwait(false);
        return remote is null ? null : await MapRemoteAccountAsync(db, remote, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ClientAccountView> MapLocalAccountAsync(
        FederationDbContext db,
        LocalActor actor,
        string localDomain,
        CancellationToken cancellationToken)
    {
        long followers = await db.FollowRelations.LongCountAsync(x => x.FollowedIri == actor.Iri && x.State == FollowState.Accepted, cancellationToken).ConfigureAwait(false);
        long following = await db.FollowRelations.LongCountAsync(x => x.FollowerIri == actor.Iri && x.State == FollowState.Accepted, cancellationToken).ConfigureAwait(false);
        long statuses = await db.Objects.LongCountAsync(x => x.OwnerIri == actor.Iri && !x.IsDeleted, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? last = await db.Objects.Where(x => x.OwnerIri == actor.Iri && !x.IsDeleted).MaxAsync(x => (DateTimeOffset?)x.PublishedAt, cancellationToken).ConfigureAwait(false);
        return new ClientAccountView(
            Id: actor.Id,
            Username: actor.Username,
            Acct: actor.Username,
            DisplayName: actor.DisplayName,
            Locked: actor.ManuallyApprovesFollowers,
            Bot: actor.Kind is ActorKind.Service or ActorKind.Application,
            Discoverable: actor.Discoverable,
            Group: actor.Kind == ActorKind.Group,
            CreatedAt: actor.CreatedAt,
            SummaryHtml: actor.SummaryHtml,
            Url: actor.Iri,
            Iri: actor.Iri,
            AvatarUrl: string.Empty,
            HeaderUrl: string.Empty,
            FollowersCount: followers,
            FollowingCount: following,
            PostsCount: statuses,
            LastPostAt: last,
            Emojis: EmptyEmojis,
            Fields: EmptyFields);
    }

    private static async Task<ClientAccountView> MapRemoteAccountAsync(
        FederationDbContext db,
        RemoteActor actor,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(actor.RawJson);
        JsonElement root = document.RootElement;
        string username = actor.PreferredUsername ?? new Uri(actor.Iri).Segments.Last().Trim('/');
        string domain = new Uri(actor.Iri).IdnHost;
        long statuses = await db.Objects.LongCountAsync(x => x.OwnerIri == actor.Iri && !x.IsDeleted, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? last = await db.Objects.Where(x => x.OwnerIri == actor.Iri && !x.IsDeleted).MaxAsync(x => (DateTimeOffset?)x.PublishedAt, cancellationToken).ConfigureAwait(false);
        string avatar = ReadImageUrl(root, "icon") ?? string.Empty;
        string header = ReadImageUrl(root, "image") ?? string.Empty;
        return new ClientAccountView(
            Id: actor.Id,
            Username: username,
            Acct: username + "@" + domain,
            DisplayName: ReadString(root, "name") ?? username,
            Locked: root.TryGetProperty("manuallyApprovesFollowers", out JsonElement locked) && locked.ValueKind == JsonValueKind.True,
            Bot: actor.Type is "Service" or "Application",
            Discoverable: !root.TryGetProperty("discoverable", out JsonElement discoverable) || discoverable.ValueKind != JsonValueKind.False,
            Group: actor.Type == "Group",
            CreatedAt: actor.FetchedAt,
            SummaryHtml: ReadString(root, "summary") ?? string.Empty,
            Url: ReadString(root, "url") ?? actor.Iri,
            Iri: actor.Iri,
            AvatarUrl: avatar,
            HeaderUrl: header,
            FollowersCount: 0,
            FollowingCount: 0,
            PostsCount: statuses,
            LastPostAt: last,
            Emojis: EmptyEmojis,
            Fields: EmptyFields);
    }

    private static async Task<bool> HasRejectMediaPolicyAsync(
        FederationDbContext db,
        string actorIri,
        CancellationToken cancellationToken)
    {
        string domain = new Uri(actorIri).IdnHost;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string[] candidates = await db.DomainPolicies
            .Where(x => x.Kind == FederationPolicyKind.RejectMedia && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => x.Domain)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return candidates.Any(candidate => string.Equals(domain, candidate, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith('.' + candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ClientMediaView> ReadAttachments(
        JsonElement root,
        Guid objectId,
        DateTimeOffset createdAt,
        bool proxyRemote)
    {
        if (!root.TryGetProperty("attachment", out JsonElement value))
        {
            return [];
        }

        IEnumerable<JsonElement> entries = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
        var result = new List<ClientMediaView>();
        foreach (JsonElement entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? url = ReadUrl(entry);
            if (url is null)
            {
                continue;
            }

            string type = (ReadString(entry, "mediaType") ?? ReadString(entry, "type") ?? "unknown").ToLowerInvariant();
            string sourceToken = PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(url))[..32];
            Guid? localMediaId = proxyRemote ? null : ReadLocalMediaId(url);
            bool useProxy = localMediaId is null;
            string servedUrl = localMediaId is Guid mediaId
                ? $"/media/{mediaId:D}"
                : $"/media/proxy/{objectId:N}/{sourceToken}";
            result.Add(new ClientMediaView(
                Id: localMediaId ?? new Guid(Convert.FromHexString(sourceToken)),
                CreatedAt: createdAt,
                MediaType: type,
                Url: servedUrl,
                PreviewUrl: servedUrl,
                RemoteUrl: useProxy ? url : null,
                Description: ReadString(entry, "name"),
                Blurhash: ReadString(entry, "blurhash"),
                Width: ReadInteger(entry, "width"),
                Height: ReadInteger(entry, "height"),
                Size: ReadLong(entry, "size")));
        }

        return result;
    }

    private static Guid? ReadLocalMediaId(string value)
    {
        string path;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute))
        {
            path = absolute.AbsolutePath;
        }
        else
        {
            int queryOrFragment = value.IndexOfAny(['?', '#']);
            path = queryOrFragment < 0 ? value : value[..queryOrFragment];
        }

        const string prefix = "/media/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        ReadOnlySpan<char> identifier = path.AsSpan(prefix.Length);
        int slash = identifier.IndexOf('/');
        if (slash >= 0)
        {
            identifier = identifier[..slash];
        }

        return Guid.TryParse(identifier, out Guid mediaId) && mediaId != Guid.Empty ? mediaId : null;
    }

    private static string? ReadImageUrl(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        foreach (string candidate in EnumerateImageUrls(value))
        {
            try
            {
                return CanonicalIri.RequireAbsoluteHttp(candidate, property);
            }
            catch (DomainException)
            {
                // A malformed leading image candidate must not hide a later safe candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateImageUrls(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (value.GetString() is { } direct)
            {
                yield return direct;
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                foreach (string nested in EnumerateImageUrls(item))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (value.TryGetProperty("url", out JsonElement url))
        {
            foreach (string nested in EnumerateImageUrls(url))
            {
                yield return nested;
            }
        }

        if (value.TryGetProperty("href", out JsonElement href) &&
            href.ValueKind == JsonValueKind.String &&
            href.GetString() is { } directHref)
        {
            yield return directHref;
        }
    }

    private static string? ReadUrl(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().Select(ReadUrl).FirstOrDefault(candidate => candidate is not null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!value.TryGetProperty("url", out JsonElement url))
        {
            return ReadString(value, "href");
        }

        return url.ValueKind switch
        {
            JsonValueKind.String => url.GetString(),
            JsonValueKind.Object => ReadUrl(url),
            JsonValueKind.Array => url.EnumerateArray().Select(ReadUrl).FirstOrDefault(candidate => candidate is not null),
            _ => null
        };
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.True;

    private static int? ReadInteger(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static long? ReadLong(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : null;

    private static (string? Text, string? Format) ReadSource(JsonElement root)
    {
        if (!root.TryGetProperty("source", out JsonElement source))
        {
            return (null, null);
        }

        return source.ValueKind switch
        {
            JsonValueKind.String => (source.GetString(), ReadString(root, "sourceMediaType")),
            JsonValueKind.Object => (ReadString(source, "content"), ReadString(source, "mediaType")),
            _ => (null, null)
        };
    }
}
