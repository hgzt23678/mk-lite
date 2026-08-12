using System.Net;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.MisskeyApi;

public sealed partial class MisskeyQueryService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClientApiQueryService query,
    IRemoteInstanceQueryService remoteInstances,
    IClientNotificationService notifications,
    IExternalEntityIdService externalIds,
    IHashtagRepository hashtags,
    FederationOptions federation)
{
    public Task<string?> FindViewerActorIriAsync(string username, CancellationToken cancellationToken) =>
        query.FindLocalActorIriAsync(username, cancellationToken);

    public async Task<IReadOnlyList<MisskeyFederationInstance>> ReadFederationInstancesAsync(
        MisskeyFederationInstancesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        (RemoteInstanceSortField field, bool descending) = request.Sort switch
        {
            "+pubSub" => (RemoteInstanceSortField.Followers, true),
            "-pubSub" => (RemoteInstanceSortField.Followers, false),
            "+notes" => (RemoteInstanceSortField.Notes, true),
            "-notes" => (RemoteInstanceSortField.Notes, false),
            "+users" => (RemoteInstanceSortField.Users, true),
            "-users" => (RemoteInstanceSortField.Users, false),
            "+following" => (RemoteInstanceSortField.Following, true),
            "-following" => (RemoteInstanceSortField.Following, false),
            "+followers" => (RemoteInstanceSortField.Followers, true),
            "-followers" => (RemoteInstanceSortField.Followers, false),
            "+caughtAt" => (RemoteInstanceSortField.Created, true),
            "-caughtAt" => (RemoteInstanceSortField.Created, false),
            "+lastCommunicatedAt" => (RemoteInstanceSortField.LastCommunicated, true),
            "-lastCommunicatedAt" => (RemoteInstanceSortField.LastCommunicated, false),
            _ => (RemoteInstanceSortField.Created, true)
        };
        IReadOnlyList<RemoteInstanceView> values = await remoteInstances.ReadAsync(
            new(
                request.Host,
                request.Blocked,
                request.NotResponding,
                request.Suspended,
                request.Federating,
                request.Subscribing,
                request.Publishing,
                field,
                descending,
                request.Offset,
                request.Limit),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> ids = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.FederationInstance,
            values.Select(value => (value.Id, value.CaughtAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return values.Select(value => new MisskeyFederationInstance(
            ids[value.Id],
            value.CaughtAt,
            value.Host,
            value.UsersCount,
            value.NotesCount,
            value.FollowingCount,
            value.FollowersCount,
            value.LatestRequestSentAt,
            value.LastCommunicatedAt,
            value.IsNotResponding,
            value.IsSuspended,
            value.IsBlocked,
            value.SoftwareName,
            value.SoftwareVersion,
            value.OpenRegistrations,
            value.Name,
            value.Description,
            value.MaintainerName,
            value.MaintainerEmail,
            value.IconUrl,
            value.FaviconUrl,
            value.ThemeColor,
            value.InfoUpdatedAt)).ToArray();
    }

    public async Task<object> CreateRenoteProjectionAsync(
        string username,
        ClientPostView target,
        CancellationToken cancellationToken)
    {
        string actorIri = await FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Authenticated account has no local actor.");
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AnnounceRelation relation = await db.AnnounceRelations.AsNoTracking()
            .Where(x => x.ActorIri == actorIri && x.ObjectIri == target.Iri && x.State == FederatedRelationState.Active)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        ActivityRecord activity = await db.Activities.AsNoTracking()
            .SingleAsync(x => x.Iri == relation.ActivityIri, cancellationToken).ConfigureAwait(false);
        object? me = await FindMeAsync(username, cancellationToken).ConfigureAwait(false);
        object original = await MapNoteAsync(target, actorIri, cancellationToken).ConfigureAwait(false);
        string activityId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            activity.Id,
            activity.OccurredAt,
            cancellationToken).ConfigureAwait(false);
        LocalActor localActor = await db.LocalActors.AsNoTracking()
            .SingleAsync(x => x.Iri == actorIri, cancellationToken).ConfigureAwait(false);
        string userId = await MapActorIdAsync(localActor.Id, localActor.CreatedAt, cancellationToken).ConfigureAwait(false);
        string targetId = await MapPostIdAsync(target.Id, target.CreatedAt, cancellationToken).ConfigureAwait(false);
        return new
        {
            id = activityId,
            createdAt = activity.OccurredAt,
            userId,
            user = me,
            text = (string?)null,
            cw = (string?)null,
            visibility = "public",
            localOnly = false,
            visibleUserIds = Array.Empty<string>(),
            renoteCount = 0,
            repliesCount = 0,
            reactions = new Dictionary<string, long>(),
            reactionEmojis = new Dictionary<string, string>(),
            emojis = new Dictionary<string, string>(),
            fileIds = Array.Empty<string>(),
            files = Array.Empty<object>(),
            replyId = (string?)null,
            renoteId = targetId,
            renote = original,
            uri = activity.Iri,
            url = activity.Iri,
            myReaction = (string?)null
        };
    }

    public async Task<object?> FindMeAsync(string username, CancellationToken cancellationToken)
    {
        ClientAccountView? account = await query.FindAccountByLookupAsync(
            username,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        return account is null ? null : await MapAccountAsync(account, detailed: true, isMe: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> FindUserAsync(
        string? userId,
        string? username,
        string? host,
        CancellationToken cancellationToken)
    {
        ClientAccountView? account = null;
        Guid? id = string.IsNullOrWhiteSpace(userId)
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                userId,
                cancellationToken).ConfigureAwait(false);
        if (id is not null)
        {
            account = await query.FindAccountByIdAsync(id.Value, federation.PublicBaseUri.IdnHost, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(username))
        {
            string lookup = string.IsNullOrWhiteSpace(host) ? username : username + "@" + host;
            account = await query.FindAccountByLookupAsync(lookup, federation.PublicBaseUri.IdnHost, cancellationToken).ConfigureAwait(false);
        }

        return account is null ? null : await MapAccountAsync(account, detailed: true, isMe: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<object>> SearchUsersAsync(
        string username,
        string? host,
        int limit,
        bool detail,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientAccountView> accounts = await query.SearchAccountsByUsernameAsync(
            username,
            host,
            limit,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        var users = new List<object>(accounts.Count);
        foreach (ClientAccountView account in accounts)
        {
            users.Add(await MapAccountAsync(account, detailed: detail, isMe: false, cancellationToken).ConfigureAwait(false));
        }

        return users;
    }

    public async Task<IReadOnlyList<object>?> ReadUserFollowRelationsAsync(
        MisskeyUserFollowRelationsRequest request,
        bool followers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid? accountId = await ResolveAccountIdAsync(request, cancellationToken).ConfigureAwait(false);
        if (accountId is null)
        {
            return null;
        }

        Guid? beforeId = await ResolveFollowRelationIdAsync(request.UntilId, cancellationToken).ConfigureAwait(false);
        Guid? afterId = await ResolveFollowRelationIdAsync(request.SinceId, cancellationToken).ConfigureAwait(false);
        ClientPage<ClientFollowRelationView> page = await query.ReadFollowRelationsAsync(
            accountId.Value,
            followers,
            beforeId,
            afterId,
            request.Limit,
            cancellationToken).ConfigureAwait(false);
        var result = new List<object>(page.Items.Count);
        foreach (ClientFollowRelationView relation in page.Items)
        {
            string relationId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.FollowRelation,
                relation.Id,
                relation.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            string followerId = await MapActorIdAsync(
                relation.Follower.Id,
                relation.Follower.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            string followeeId = await MapActorIdAsync(
                relation.Followee.Id,
                relation.Followee.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            object user = await MapAccountAsync(
                followers ? relation.Follower : relation.Followee,
                detailed: true,
                isMe: false,
                cancellationToken).ConfigureAwait(false);
            result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = relationId,
                ["createdAt"] = relation.CreatedAt,
                ["followeeId"] = followeeId,
                ["followerId"] = followerId,
                [followers ? "follower" : "followee"] = user
            });
        }

        return result;
    }

    private async Task<Guid?> ResolveAccountIdAsync(
        MisskeyUserFollowRelationsRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            return await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                request.UserId,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return null;
        }

        ClientAccountView? account = await query.FindAccountByLookupAsync(
            string.IsNullOrWhiteSpace(request.Host)
                ? request.Username
                : request.Username + "@" + request.Host,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        return account?.Id;
    }

    private Task<Guid?> ResolveFollowRelationIdAsync(
        string? externalId,
        CancellationToken cancellationToken) => string.IsNullOrWhiteSpace(externalId)
        ? Task.FromResult<Guid?>(null)
        : externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.FollowRelation,
            externalId,
            cancellationToken);

    public async Task<IReadOnlyList<object>> TrendHashtagsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<HashtagTrend> trends = await hashtags.TrendAsync(
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return trends.Select(trend => new Dictionary<string, object?>
        {
            ["tag"] = trend.Tag,
            ["chart"] = trend.Chart,
            ["usersCount"] = trend.UsersCount
        }).ToArray();
    }

    public async Task<Guid?> ResolveMediaIdAsync(string externalId, CancellationToken cancellationToken)
    {
        Guid? internalId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Media,
            externalId,
            cancellationToken).ConfigureAwait(false);
        return internalId;
    }

    public async Task<object> MapDriveFileAsync(
        ClientDriveFileView file,
        CancellationToken cancellationToken)
    {
        string externalId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Media,
            file.Id,
            file.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["id"] = externalId,
            ["name"] = file.Name,
            ["type"] = file.MediaType,
            ["md5"] = file.Md5,
            ["size"] = file.Size,
            ["url"] = file.Url,
            ["isSensitive"] = file.IsSensitive,
            ["blurhash"] = file.Blurhash,
            ["properties"] = new Dictionary<string, object?>
            {
                ["width"] = file.Width,
                ["height"] = file.Height
            },
            ["folderId"] = file.FolderId?.ToString("D"),
            ["comment"] = file.Comment,
            ["createdAt"] = file.CreatedAt
        };
    }

    public async Task<MisskeyRelationshipStreamProjection?> FindStreamRelationshipUserAsync(
        Guid accountId,
        string viewerActorIri,
        CancellationToken cancellationToken)
    {
        string viewer = CanonicalIri.RequireAbsoluteHttp(viewerActorIri, nameof(viewerActorIri));
        ClientAccountView? account = await query.FindAccountByIdAsync(
            accountId,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        if (account is null || string.Equals(account.Iri, viewer, StringComparison.Ordinal))
        {
            return null;
        }

        ClientRelationshipView? relationship = await query.FindRelationshipAsync(
            viewer,
            accountId,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        if (relationship is null)
        {
            return null;
        }

        Dictionary<string, object?> user = await MapAccountAsync(
            account,
            detailed: true,
            isMe: false,
            cancellationToken).ConfigureAwait(false);
        AddRelationshipFields(user, relationship);
        return new(
            relationship.Following || relationship.Requested ? "follow" : "unfollow",
            user);
    }

    public async Task<IReadOnlyList<object>?> FindUsersAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var result = new List<object>(userIds.Count);
        foreach (string userId in userIds)
        {
            object? user = await FindUserAsync(userId, null, null, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                return null;
            }

            result.Add(user);
        }

        return result;
    }

    public async Task<object?> FindRelationshipAsync(
        string username,
        string userId,
        CancellationToken cancellationToken)
    {
        string? owner = await query.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (owner is null || accountId is null)
        {
            return null;
        }

        ClientRelationshipView? relationship = await query.FindRelationshipAsync(
            owner,
            accountId.Value,
            federation.PublicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        return relationship is null
            ? null
            : new
            {
                id = userId,
                isFollowing = relationship.Following,
                hasPendingFollowRequestFromYou = relationship.Requested,
                hasPendingFollowRequestToYou = relationship.RequestedBy,
                isFollowed = relationship.FollowedBy,
                isBlocking = relationship.Blocking,
                isBlocked = relationship.BlockedBy,
                isMuted = relationship.Muting
            };
    }

    public async Task<object?> FindNoteAsync(
        string noteId,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            return null;
        }

        ClientPostView? status = await query.FindPostAsync(id.Value, viewerActorIri, cancellationToken).ConfigureAwait(false);
        return status is null ? null : await MapNoteAsync(status, viewerActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> FindNoteByInternalIdAsync(
        Guid noteId,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ClientPostView? status = await query.FindPostAsync(noteId, viewerActorIri, cancellationToken).ConfigureAwait(false);
        return status is null ? null : await MapNoteAsync(status, viewerActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> FindStreamNoteAsync(
        Guid noteId,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        ClientPostView? status = await query.FindStreamPostAsync(
            noteId,
            viewerActorIri,
            audience,
            localOnly,
            cancellationToken).ConfigureAwait(false);
        return status is null ? null : await MapNoteAsync(status, viewerActorIri, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CanReceiveStreamEventAsync(
        StreamEvent streamEvent,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken) =>
        query.CanReceiveStreamEventAsync(streamEvent, viewerActorIri, audience, localOnly, cancellationToken);

    public Task<string> MapStreamNoteIdAsync(
        Guid id,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            id,
            occurredAt,
            cancellationToken);

    public async Task<string?> MapStreamActorIdAsync(
        string actorIri,
        CancellationToken cancellationToken)
    {
        ClientAccountView? account = await query.FindAccountByIriAsync(actorIri, cancellationToken).ConfigureAwait(false);
        return account is null
            ? null
            : await MapActorIdAsync(account.Id, account.CreatedAt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<object>> ReadNotificationsAsync(
        string recipientActorIri,
        string? untilId,
        int limit,
        bool unreadOnly,
        bool markAsRead,
        IReadOnlyCollection<string>? includeTypes,
        IReadOnlyCollection<string>? excludeTypes,
        CancellationToken cancellationToken)
    {
        Guid? beforeId = string.IsNullOrWhiteSpace(untilId)
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Notification,
                untilId,
                cancellationToken).ConfigureAwait(false);
        if (untilId is not null && beforeId is null)
        {
            return [];
        }

        ClientPage<ClientNotificationView> page = await notifications.ReadAsync(
            recipientActorIri,
            new(
                beforeId,
                Math.Clamp(limit, 1, 100),
                unreadOnly,
                markAsRead,
                ParseNotificationKinds(includeTypes),
                ParseNotificationKinds(excludeTypes)),
            cancellationToken).ConfigureAwait(false);
        var result = new List<object>(page.Items.Count);
        foreach (ClientNotificationView item in page.Items)
        {
            result.Add(await MapNotificationAsync(item, recipientActorIri, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    public async Task<object?> FindStreamNotificationAsync(
        Guid notificationId,
        string recipientActorIri,
        CancellationToken cancellationToken)
    {
        ClientNotificationView? item = await notifications.FindAsync(
            recipientActorIri,
            notificationId,
            cancellationToken).ConfigureAwait(false);
        return item is null ? null : await MapNotificationAsync(item, recipientActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkNotificationsReadAsync(
        string recipientActorIri,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var resolved = new List<Guid>(ids.Count);
        foreach (string id in ids.Distinct(StringComparer.Ordinal))
        {
            Guid? internalId = await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Notification,
                id,
                cancellationToken).ConfigureAwait(false);
            if (internalId is null)
            {
                return false;
            }

            resolved.Add(internalId.Value);
        }

        return await notifications.MarkReadAsync(
            recipientActorIri,
            resolved,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<int> MarkAllNotificationsReadAsync(string recipientActorIri, CancellationToken cancellationToken) =>
        notifications.MarkAllReadAsync(recipientActorIri, DateTimeOffset.UtcNow, cancellationToken);

    private async Task<object> MapNotificationAsync(
        ClientNotificationView value,
        string recipientActorIri,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Notification,
            value.Id,
            value.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        string userId = await MapActorIdAsync(value.Account.Id, value.Account.CreatedAt, cancellationToken).ConfigureAwait(false);
        return new
        {
            id,
            createdAt = value.CreatedAt,
            type = value.Kind switch
            {
                UserNotificationKind.Follow => "follow",
                UserNotificationKind.Favourite or UserNotificationKind.Reaction => "reaction",
                UserNotificationKind.Reblog => "renote",
                UserNotificationKind.Poll => "pollEnded",
                UserNotificationKind.Application => "app",
                _ => "mention"
            },
            userId,
            user = await MapAccountAsync(value.Account, detailed: false, isMe: false, cancellationToken).ConfigureAwait(false),
            note = value.Post is null ? null : await MapNoteAsync(value.Post, recipientActorIri, cancellationToken).ConfigureAwait(false),
            reaction = value.Reaction,
            isRead = value.IsRead
        };
    }

    private static HashSet<UserNotificationKind>? ParseNotificationKinds(IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var result = new HashSet<UserNotificationKind>();
        foreach (string value in values)
        {
            if (value == "reaction")
            {
                result.Add(UserNotificationKind.Favourite);
                result.Add(UserNotificationKind.Reaction);
                continue;
            }

            UserNotificationKind? kind = value switch
            {
                "follow" => UserNotificationKind.Follow,
                "renote" => UserNotificationKind.Reblog,
                "pollEnded" => UserNotificationKind.Poll,
                "app" => UserNotificationKind.Application,
                "mention" or "reply" or "quote" => UserNotificationKind.Mention,
                _ => null
            };
            if (kind is not null)
            {
                result.Add(kind.Value);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<object>> ReadTimelineAsync(
        string kind,
        string? viewerActorIri,
        string? untilId,
        int limit,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 40);
        Guid? cursor = string.IsNullOrWhiteSpace(untilId)
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Post,
                untilId,
                cancellationToken).ConfigureAwait(false);
        if (untilId is not null && cursor is null)
        {
            return [];
        }

        ClientPage<ClientPostView> page = kind switch
        {
            "home" when viewerActorIri is not null => await query.ReadHomeTimelineAsync(
                viewerActorIri,
                cursor,
                safeLimit,
                cancellationToken).ConfigureAwait(false),
            _ => await query.ReadPublicTimelineAsync(
                cursor,
                safeLimit,
                localOnly,
                cancellationToken).ConfigureAwait(false)
        };
        var notes = new List<object>(page.Items.Count);
        foreach (ClientPostView status in page.Items)
        {
            notes.Add(await MapNoteAsync(status, viewerActorIri, cancellationToken).ConfigureAwait(false));
        }

        return notes;
    }

    public async Task<IReadOnlyList<object>?> ReadUserNotesAsync(
        string userId,
        string? viewerActorIri,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            return null;
        }

        Guid? cursor = string.IsNullOrWhiteSpace(untilId)
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Post,
                untilId,
                cancellationToken).ConfigureAwait(false);
        if (untilId is not null && cursor is null)
        {
            return [];
        }

        ClientPage<ClientPostView> page = await query.ReadAccountPostsAsync(
            id.Value,
            federation.PublicBaseUri.IdnHost,
            cursor,
            Math.Clamp(limit, 1, 40),
            viewerActorIri,
            cancellationToken).ConfigureAwait(false);
        var notes = new List<object>(page.Items.Count);
        foreach (ClientPostView status in page.Items)
        {
            notes.Add(await MapNoteAsync(status, viewerActorIri, cancellationToken).ConfigureAwait(false));
        }

        return notes;
    }

    public async Task<IReadOnlyList<object>> ReadReactionsAsync(
        string noteId,
        int limit,
        string? type,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        Guid? objectId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            cancellationToken).ConfigureAwait(false);
        if (objectId is null)
        {
            return [];
        }

        if (await query.FindPostAsync(objectId.Value, viewerActorIri, cancellationToken).ConfigureAwait(false) is null)
        {
            return [];
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? objectIri = await db.Objects.Where(x => x.Id == objectId.Value && !x.IsDeleted)
            .Select(x => x.Iri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (objectIri is null)
        {
            return [];
        }

        LikeRelation[] relations = await db.LikeRelations
            .Where(x => x.ObjectIri == objectIri && x.State == FederatedRelationState.Active)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100) * 4)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        EmojiReactionRelation[] emojiRelations = await db.EmojiReactionRelations
            .Where(x => x.ObjectIri == objectIri && x.State == FederatedRelationState.Active)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100) * 4)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        int pageSize = Math.Clamp(limit, 1, 100);
        var page = relations
            .Select(x => (x.Id, x.CreatedAt, x.ActorIri, Reaction: x.EffectiveReaction))
            .Concat(emojiRelations.Select(x => (x.Id, x.CreatedAt, x.ActorIri, Reaction: x.Reaction)))
            .Where(x => type is null || x.Reaction == type)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(pageSize)
            .ToArray();
        var result = new List<object>(page.Length);
        foreach ((Guid id, DateTimeOffset createdAt, string actorIri, string reaction) in page)
        {
            ClientAccountView? account = await query.FindAccountByIriAsync(actorIri, cancellationToken).ConfigureAwait(false);
            if (account is not null)
            {
                result.Add(new
                {
                    id = await externalIds.GetOrCreateAsync(
                        ApiDialect.Misskey,
                        ExternalEntityType.Activity,
                        id,
                        createdAt,
                        cancellationToken).ConfigureAwait(false),
                    createdAt,
                    user = await MapAccountAsync(account, detailed: false, isMe: false, cancellationToken).ConfigureAwait(false),
                    type = reaction
                });
            }
        }

        return result;
    }

    private async Task<object> MapNoteAsync(
        ClientPostView status,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string objectIri = status.Iri;
        LikeRelation[] likes = await db.LikeRelations
            .Where(x => x.ObjectIri == objectIri && x.State == FederatedRelationState.Active)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        EmojiReactionRelation[] emojiReactions = await db.EmojiReactionRelations
            .Where(x => x.ObjectIri == objectIri && x.State == FederatedRelationState.Active)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var reactions = likes.Select(x => x.EffectiveReaction)
            .Concat(emojiReactions.Select(x => x.Reaction))
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);
        string? myReaction = viewerActorIri is null
            ? null
            : likes.FirstOrDefault(x => string.Equals(x.ActorIri, viewerActorIri, StringComparison.Ordinal))?.EffectiveReaction;
        myReaction ??= viewerActorIri is null
            ? null
            : emojiReactions.FirstOrDefault(x => string.Equals(x.ActorIri, viewerActorIri, StringComparison.Ordinal))?.Reaction;
        var emojis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (LikeRelation like in likes.Where(x => x.CustomEmojiUrl is not null))
        {
            string key = like.EffectiveReaction.Trim(':');
            string token = PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(like.CustomEmojiUrl!))[..32];
            emojis.TryAdd(key, $"/media/proxy/{status.Id:N}/{token}");
        }


        foreach (EmojiReactionRelation reaction in emojiReactions.Where(x => x.CustomEmojiUrl is not null))
        {
            string key = reaction.Reaction.Trim(':');
            string token = PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(reaction.CustomEmojiUrl!))[..32];
            emojis.TryAdd(key, $"/media/proxy/{status.Id:N}/{token}");
        }

        string noteId = await MapPostIdAsync(status.Id, status.CreatedAt, cancellationToken).ConfigureAwait(false);
        string userId = await MapActorIdAsync(status.Account.Id, status.Account.CreatedAt, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> visibleUserIds = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            (status.VisibleRecipients ?? []).Select(account => (account.Id, account.CreatedAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        object user = await MapAccountAsync(status.Account, detailed: false, isMe: false, cancellationToken).ConfigureAwait(false);
        var files = new List<object>(status.Attachments.Count);
        var fileIds = new List<string>(status.Attachments.Count);
        foreach (ClientMediaView media in status.Attachments)
        {
            string mediaId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Media,
                media.Id,
                media.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            fileIds.Add(mediaId);
            files.Add(new
            {
                id = mediaId,
                createdAt = media.CreatedAt,
                name = media.Description ?? mediaId,
                type = media.MediaType,
                md5 = (string?)null,
                size = media.Size,
                isSensitive = status.Sensitive,
                blurhash = media.Blurhash,
                url = media.Url,
                thumbnailUrl = media.PreviewUrl,
                comment = media.Description,
                properties = new { width = media.Width, height = media.Height }
            });
        }

        string? replyId = status.InReplyToId is null
            ? null
            : await MapPostIdAsync(status.InReplyToId.Value, status.CreatedAt, cancellationToken).ConfigureAwait(false);
        object? poll = status.Poll is null ? null : await MapPollAsync(status.Poll, cancellationToken).ConfigureAwait(false);
        return new
        {
            id = noteId,
            createdAt = status.CreatedAt,
            userId,
            user,
            text = status.SourceText ?? HtmlToText(status.SanitizedHtml),
            cw = string.IsNullOrEmpty(status.ContentWarning) ? null : status.ContentWarning,
            visibility = status.Visibility switch
            {
                Visibility.Unlisted => "home",
                Visibility.FollowersOnly => "followers",
                Visibility.MentionedOnly => "specified",
                _ => "public"
            },
            localOnly = status.LocalOnly,
            visibleUserIds = (status.VisibleRecipients ?? [])
                .Where(account => visibleUserIds.ContainsKey(account.Id))
                .Select(account => visibleUserIds[account.Id])
                .ToArray(),
            reactionAcceptance = (string?)null,
            renoteCount = status.AnnouncesCount,
            repliesCount = status.RepliesCount,
            reactions,
            reactionEmojis = emojis,
            emojis,
            fileIds,
            files,
            replyId,
            renoteId = (string?)null,
            poll,
            uri = status.Iri,
            url = status.Url,
            myReaction
        };
    }

    private async Task<Dictionary<string, object?>> MapAccountAsync(
        ClientAccountView account,
        bool detailed,
        bool isMe,
        CancellationToken cancellationToken)
    {
        bool local = string.Equals(new Uri(account.Iri).IdnHost, federation.PublicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase);
        string? host = local ? null : new Uri(account.Iri).IdnHost;
        string id = await MapActorIdAsync(account.Id, account.CreatedAt, cancellationToken).ConfigureAwait(false);
        string? avatar = local
            ? EmptyToNull(account.AvatarUrl)
            : CreateRemoteActorMediaProxyPath(id, account.AvatarUrl);
        var value = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = string.IsNullOrEmpty(account.DisplayName) ? account.Username : account.DisplayName,
            ["username"] = account.Username,
            ["host"] = host,
            ["avatarUrl"] = avatar,
            ["avatarBlurhash"] = null,
            ["avatarColor"] = null,
            ["isAdmin"] = false,
            ["isModerator"] = false,
            ["isBot"] = account.Bot,
            ["isCat"] = false,
            ["instance"] = host is null ? null : new { name = host, softwareName = (string?)null, softwareVersion = (string?)null, iconUrl = (string?)null, faviconUrl = (string?)null, themeColor = (string?)null },
            ["emojis"] = new Dictionary<string, string>(),
            ["onlineStatus"] = "unknown"
        };
        if (detailed)
        {
            value["url"] = account.Url;
            value["uri"] = account.Iri;
            value["createdAt"] = account.CreatedAt;
            value["updatedAt"] = (DateTimeOffset?)null;
            value["lastFetchedAt"] = (DateTimeOffset?)null;
            value["bannerUrl"] = local
                ? EmptyToNull(account.HeaderUrl)
                : CreateRemoteActorMediaProxyPath(id, account.HeaderUrl);
            value["bannerBlurhash"] = null;
            value["isLocked"] = account.Locked;
            value["isSilenced"] = false;
            value["isSuspended"] = false;
            value["description"] = HtmlToText(account.SummaryHtml);
            value["location"] = null;
            value["birthday"] = null;
            value["fields"] = account.Fields;
            value["followersCount"] = account.FollowersCount;
            value["followingCount"] = account.FollowingCount;
            value["notesCount"] = account.PostsCount;
            value["pinnedNoteIds"] = Array.Empty<string>();
            value["pinnedNotes"] = Array.Empty<object>();
            value["pinnedPageId"] = null;
            value["pinnedPage"] = null;
            value["publicReactions"] = true;
            value["ffVisibility"] = "public";
            value["twoFactorEnabled"] = false;
            value["usePasswordLessLogin"] = true;
            value["securityKeys"] = false;
            value["roles"] = Array.Empty<object>();
        }

        if (isMe)
        {
            long unreadNotifications = await notifications.CountUnreadAsync(account.Iri, cancellationToken).ConfigureAwait(false);
            ClientPage<ClientNotificationView> unreadMentions = await notifications.ReadAsync(
                account.Iri,
                new(
                    BeforeId: null,
                    Limit: 1,
                    UnreadOnly: true,
                    MarkAsRead: false,
                    IncludeKinds: new HashSet<UserNotificationKind> { UserNotificationKind.Mention },
                    ExcludeKinds: null),
                cancellationToken).ConfigureAwait(false);
            await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            bool pendingReceivedFollow = await db.FollowRelations.AnyAsync(x =>
                x.FollowedIri == account.Iri && x.State == FollowState.Pending,
                cancellationToken).ConfigureAwait(false);
            value["email"] = null;
            value["hasPendingReceivedFollowRequest"] = pendingReceivedFollow;
            value["hasUnreadAnnouncement"] = false;
            value["hasUnreadAntenna"] = false;
            value["hasUnreadMentions"] = unreadMentions.Items.Count > 0;
            value["hasUnreadMessagingMessage"] = false;
            value["hasUnreadNotification"] = unreadNotifications > 0;
            value["hasUnreadSpecifiedNotes"] = false;
        }

        return value;
    }

    private static void AddRelationshipFields(
        Dictionary<string, object?> user,
        ClientRelationshipView relationship)
    {
        user["isFollowing"] = relationship.Following;
        user["hasPendingFollowRequestFromYou"] = relationship.Requested;
        user["hasPendingFollowRequestToYou"] = relationship.RequestedBy;
        user["isFollowed"] = relationship.FollowedBy;
        user["isBlocking"] = relationship.Blocking;
        user["isBlocked"] = relationship.BlockedBy;
        user["isMuted"] = relationship.Muting;
    }

    private static string? CreateRemoteActorMediaProxyPath(string actorId, string? sourceIri)
    {
        if (string.IsNullOrWhiteSpace(sourceIri))
        {
            return null;
        }

        try
        {
            string canonical = CanonicalIri.RequireAbsoluteHttp(sourceIri, nameof(sourceIri));
            string sourceToken = RemoteMediaSourceToken.Create(canonical);
            return $"/media/proxy/actor/{Uri.EscapeDataString(actorId)}/{sourceToken}";
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string HtmlToText(string value)
    {
        string withBreaks = BreakPattern().Replace(value, "\n");
        return WebUtility.HtmlDecode(TagPattern().Replace(withBreaks, string.Empty)).Trim();
    }

    private Task<string> MapActorIdAsync(Guid id, DateTimeOffset timestamp, CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(ApiDialect.Misskey, ExternalEntityType.Actor, id, timestamp, cancellationToken);

    private Task<string> MapPostIdAsync(Guid id, DateTimeOffset timestamp, CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(ApiDialect.Misskey, ExternalEntityType.Post, id, timestamp, cancellationToken);

    private async Task<object> MapPollAsync(ClientPollView poll, CancellationToken cancellationToken) => new
    {
        id = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Poll,
            poll.Id,
            poll.ExpiresAt ?? DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false),
        expiresAt = poll.ExpiresAt,
        multiple = poll.Multiple,
        choices = poll.Options.Select((option, index) => new
        {
            text = option.Title,
            votes = option.VotesCount,
            isVoted = poll.OwnVotes.Contains(index)
        }).ToArray()
    };

    [GeneratedRegex("<(?:br\\s*/?|/p|/div)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BreakPattern();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex TagPattern();
}

public sealed record MisskeyRelationshipStreamProjection(string Type, object User);
