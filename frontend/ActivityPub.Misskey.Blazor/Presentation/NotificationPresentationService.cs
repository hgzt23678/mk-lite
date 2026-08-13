using System.Net;
using System.Text.RegularExpressions;
#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Server;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public enum MisskeyNotificationType
{
    Follow,
    FollowRequestAccepted,
    ReceiveFollowRequest,
    GroupInvited,
    Renote,
    Reply,
    Mention,
    Quote,
    PollVote,
    PollEnded,
    Reaction,
    App
}

public sealed record NotificationNoteViewModel(
    Guid InternalId,
    string Id,
    DateTimeOffset CreatedAt,
    NoteAuthorViewModel Author,
    string Text,
    string? ContentWarning,
    bool HasReply,
    int MediaCount,
    bool HasPoll,
    IReadOnlyDictionary<string, string> Emojis,
    NotificationNoteViewModel? Renote);

public sealed record NotificationViewModel(
    Guid InternalId,
    string Id,
    DateTimeOffset CreatedAt,
    MisskeyNotificationType Type,
    bool IsRead,
    NoteAuthorViewModel? User,
    NotificationNoteViewModel? Note,
    string? Reaction,
    UserPreviewViewModel? FollowUser = null,
    string? Header = null,
    string? Body = null,
    string? IconUrl = null,
    string? BlockedReason = null,
    NoteViewModel? FullNote = null)
{
    public string TypeName => Type switch
    {
        MisskeyNotificationType.Follow => "follow",
        MisskeyNotificationType.FollowRequestAccepted => "followRequestAccepted",
        MisskeyNotificationType.ReceiveFollowRequest => "receiveFollowRequest",
        MisskeyNotificationType.GroupInvited => "groupInvited",
        MisskeyNotificationType.Renote => "renote",
        MisskeyNotificationType.Reply => "reply",
        MisskeyNotificationType.Mention => "mention",
        MisskeyNotificationType.Quote => "quote",
        MisskeyNotificationType.PollVote => "pollVote",
        MisskeyNotificationType.PollEnded => "pollEnded",
        MisskeyNotificationType.Reaction => "reaction",
        MisskeyNotificationType.App => "app",
        _ => throw new ArgumentOutOfRangeException(nameof(Type))
    };
}

public sealed record NotificationPresentationQuery(
    string? UntilId,
    int Limit,
    bool UnreadOnly = false,
    IReadOnlySet<MisskeyNotificationType>? IncludeTypes = null,
    IReadOnlySet<MisskeyNotificationType>? ExcludeTypes = null);

public interface INotificationPresentationService
{
    Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
        NotificationPresentationQuery request,
        CancellationToken cancellationToken);

    Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed partial class NotificationPresentationService(
    IClientNotificationService notifications,
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    IAuthenticatedActorContext actorContext,
    IUserPreviewPresentationService userPreviews) : INotificationPresentationService
{
    public async Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
        NotificationPresentationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        Guid? beforeId = await ResolveCursorAsync(request.UntilId, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<UserNotificationKind>? included = ToDomainIncludedKinds(request.IncludeTypes);
        IReadOnlySet<UserNotificationKind>? excluded = ToDomainExcludedKinds(request.ExcludeTypes);
        int requestedLimit = Math.Clamp(request.Limit, 1, 100);
        var result = new List<NotificationViewModel>(requestedLimit);
        Guid? cursor = beforeId;
        while (result.Count < requestedLimit)
        {
            ClientPage<ClientNotificationView> page = await notifications.ReadAsync(
                actor.ActorIri,
                new(
                    cursor,
                    Math.Min(100, Math.Max(requestedLimit, requestedLimit - result.Count)),
                    request.UnreadOnly,
                    MarkAsRead: false,
                    included,
                    excluded),
                cancellationToken).ConfigureAwait(false);
            foreach (ClientNotificationView item in page.Items)
            {
                NotificationViewModel mapped = await MapAsync(
                    item,
                    actor.ActorIri,
                    cancellationToken).ConfigureAwait(false);
                if (Matches(mapped.Type, request.IncludeTypes, request.ExcludeTypes))
                {
                    result.Add(mapped);
                    if (result.Count == requestedLimit)
                    {
                        break;
                    }
                }
            }

            if (result.Count == requestedLimit || page.Next is null || page.Next.Id == cursor)
            {
                break;
            }

            cursor = page.Next.Id;
        }

        return result;
    }

    public async Task<NotificationViewModel?> FindAsync(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        if (notificationId == Guid.Empty)
        {
            throw new ArgumentException("A notification identifier is required.", nameof(notificationId));
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientNotificationView? item = await notifications.FindAsync(
            actor.ActorIri,
            notificationId,
            cancellationToken).ConfigureAwait(false);
        return item is null
            ? null
            : await MapAsync(item, actor.ActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        if (notificationId == Guid.Empty)
        {
            return false;
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        return await notifications.MarkReadAsync(
            actor.ActorIri,
            [notificationId],
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        return await notifications.MarkAllReadAsync(
            actor.ActorIri,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveCursorAsync(string? cursor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        Guid? value = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Notification,
            cursor,
            cancellationToken).ConfigureAwait(false);
        return value ?? throw new NotificationPresentationException("NOTIFICATION_CURSOR_INVALID");
    }

    private async Task<NotificationViewModel> MapAsync(
        ClientNotificationView item,
        string viewerActorIri,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Notification,
            item.Id,
            item.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        NoteAuthorViewModel user = await MapAuthorAsync(item.Account, cancellationToken).ConfigureAwait(false);
        NotificationNoteViewModel? note = item.Post is null
            ? null
            : await MapNoteAsync(item.Post, cancellationToken).ConfigureAwait(false);
        NoteViewModel? fullNote = item.Post is null
            ? null
            : await MapFullNoteAsync(
                item.Post,
                viewerActorIri,
                cancellationToken).ConfigureAwait(false);
        MisskeyNotificationType type = item.Kind switch
        {
            UserNotificationKind.Follow => MisskeyNotificationType.Follow,
            UserNotificationKind.Favourite or UserNotificationKind.Reaction => MisskeyNotificationType.Reaction,
            UserNotificationKind.Reblog => MisskeyNotificationType.Renote,
            UserNotificationKind.Poll => MisskeyNotificationType.PollEnded,
            UserNotificationKind.Mention when item.Post?.AnnouncedPost is not null => MisskeyNotificationType.Quote,
            UserNotificationKind.Mention when item.Post?.InReplyToId is not null => MisskeyNotificationType.Reply,
            UserNotificationKind.Mention => MisskeyNotificationType.Mention,
            UserNotificationKind.Application or UserNotificationKind.Update => MisskeyNotificationType.App,
            _ => throw new NotificationPresentationException("NOTIFICATION_TYPE_UNSUPPORTED")
        };
        UserPreviewViewModel? followUser = null;
        if (type == MisskeyNotificationType.Follow)
        {
            followUser = await userPreviews.ReadAsync(user.Id, cancellationToken).ConfigureAwait(false);
        }

        string? blockedReason = item.Kind switch
        {
            UserNotificationKind.Application => "NOTIFICATION_APPLICATION_PAYLOAD_UNAVAILABLE",
            UserNotificationKind.Update => "NOTIFICATION_UPDATE_PROJECTION_UNAVAILABLE",
            _ => null
        };
        return new(
            item.Id,
            id,
            item.CreatedAt,
            type,
            item.IsRead,
            user,
            note,
            type == MisskeyNotificationType.Reaction ? item.Reaction ?? "👍" : item.Reaction,
            followUser,
            Header: null,
            Body: null,
            IconUrl: null,
            blockedReason,
            fullNote);
    }

    private async Task<NoteAuthorViewModel> MapAuthorAsync(
        ClientAccountView account,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            account.Id,
            account.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> emojis = account.Emojis
            .Where(value => !string.IsNullOrWhiteSpace(value.Shortcode) && !string.IsNullOrWhiteSpace(value.Url))
            .GroupBy(value => value.Shortcode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Url, StringComparer.Ordinal);
        return new(
            id,
            account.Username,
            account.Acct,
            string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            account.AvatarUrl,
            account.Bot,
            Emojis: emojis);
    }

    private async Task<NotificationNoteViewModel> MapNoteAsync(
        ClientPostView post,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            post.Id,
            post.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        NoteAuthorViewModel author = await MapAuthorAsync(post.Account, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> emojis = post.Emojis
            .Where(value => !string.IsNullOrWhiteSpace(value.Shortcode) && !string.IsNullOrWhiteSpace(value.Url))
            .GroupBy(value => value.Shortcode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Url, StringComparer.Ordinal);
        NotificationNoteViewModel? renote = post.AnnouncedPost is null
            ? null
            : await MapNoteAsync(post.AnnouncedPost, cancellationToken).ConfigureAwait(false);
        return new(
            post.Id,
            id,
            post.CreatedAt,
            author,
            post.SourceText ?? ConvertSanitizedHtmlToText(post.SanitizedHtml),
            string.IsNullOrWhiteSpace(post.ContentWarning) ? null : post.ContentWarning,
            post.InReplyToId is not null,
            post.Attachments.Count,
            post.Poll is not null,
            emojis,
            renote);
    }

    private async Task<NoteViewModel> MapFullNoteAsync(
        ClientPostView post,
        string viewerActorIri,
        CancellationToken cancellationToken,
        bool includeReply = true)
    {
        ClientReactionSummaryView reactionSummary = await query.ReadPostReactionsAsync(
            post.Id,
            viewerActorIri,
            cancellationToken).ConfigureAwait(false);
        string noteId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            post.Id,
            post.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        NoteAuthorViewModel author = await MapAuthorAsync(post.Account, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> mediaIds = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Media,
            post.Attachments.Select(item => (item.Id, item.CreatedAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        NoteMediaViewModel[] media = post.Attachments.Select(item => new NoteMediaViewModel(
            mediaIds[item.Id],
            item.MediaType,
            item.Url,
            item.PreviewUrl,
            item.Description,
            item.Blurhash,
            item.Width,
            item.Height,
            post.Sensitive,
            item.Size)).ToArray();
        NotePollViewModel? poll = null;
        if (post.Poll is not null)
        {
            string pollId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Poll,
                post.Poll.Id,
                post.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            poll = new(
                pollId,
                post.Poll.ExpiresAt,
                post.Poll.Expired,
                post.Poll.Multiple,
                post.Poll.VotedByViewer,
                post.Poll.OwnVotes,
                post.Poll.Options.Select(option => new NotePollOptionViewModel(
                    option.Title,
                    option.VotesCount)).ToArray());
        }

        NoteViewModel? renote = post.AnnouncedPost is null
            ? null
            : await MapFullNoteAsync(
                post.AnnouncedPost,
                viewerActorIri,
                cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> visibleRecipientIds = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            (post.VisibleRecipients ?? []).Select(account => (account.Id, account.CreatedAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        string? replyId = post.InReplyToId is null
            ? null
            : await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Post,
                post.InReplyToId.Value,
                post.CreatedAt,
                cancellationToken).ConfigureAwait(false);
        NoteViewModel? reply = null;
        if (includeReply && post.InReplyToId is Guid replyPostId)
        {
            ClientPostView? replyPost = await query.FindPostAsync(
                replyPostId,
                viewerActorIri,
                cancellationToken).ConfigureAwait(false);
            if (replyPost is not null && replyPost.Id != post.Id)
            {
                reply = await MapFullNoteAsync(
                    replyPost,
                    viewerActorIri,
                    cancellationToken,
                    includeReply: false).ConfigureAwait(false);
            }
        }

        IReadOnlyDictionary<string, string> emojis = post.Emojis
            .Where(item => !string.IsNullOrWhiteSpace(item.Shortcode) && !string.IsNullOrWhiteSpace(item.Url))
            .Select(item => new KeyValuePair<string, string>(item.Shortcode, item.Url))
            .Concat(reactionSummary.CustomEmojiUrls)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        return new(
            post.Id,
            noteId,
            post.CreatedAt,
            author,
            post.SourceText ?? ConvertSanitizedHtmlToText(post.SanitizedHtml),
            string.IsNullOrWhiteSpace(post.ContentWarning) ? null : post.ContentWarning,
            FrontendVisibilityMapper.FromDomain(post.Visibility),
            replyId,
            post.RepliesCount,
            post.AnnouncesCount,
            reactionSummary.Reactions.Values.Sum(),
            reactionSummary.ViewerReaction is not null,
            reactionSummary.Reactions,
            reactionSummary.ViewerReaction,
            media,
            post.Mentions.Select(item => item.Acct).ToArray(),
            post.Hashtags.Select(item => item.Name).ToArray(),
            emojis,
            poll,
            renote,
            post.LocalOnly,
            (post.VisibleRecipients ?? [])
                .Where(account => visibleRecipientIds.ContainsKey(account.Id))
                .Select(account => visibleRecipientIds[account.Id])
                .ToArray(),
            RenoteId: renote?.Id,
            Reply: reply,
            IsMuted: post.MutedForViewer,
            RemoteUrl: post.Account.Acct.Contains('@', StringComparison.Ordinal)
                ? FirstAbsoluteHttpUrl(post.Url, post.Iri)
                : null);
    }

    private static string? FirstAbsoluteHttpUrl(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) &&
                string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri.AbsoluteUri;
            }
        }

        return null;
    }

    private static bool Matches(
        MisskeyNotificationType type,
        IReadOnlySet<MisskeyNotificationType>? included,
        IReadOnlySet<MisskeyNotificationType>? excluded) =>
        (included is null || included.Count == 0 || included.Contains(type)) &&
        (excluded is null || !excluded.Contains(type));

    private static HashSet<UserNotificationKind>? ToDomainIncludedKinds(
        IReadOnlySet<MisskeyNotificationType>? types)
    {
        if (types is null)
        {
            return null;
        }

        return types.SelectMany(MapDomainKinds)
            .ToHashSet();
    }

    private static HashSet<UserNotificationKind>? ToDomainExcludedKinds(
        IReadOnlySet<MisskeyNotificationType>? types)
    {
        if (types is null)
        {
            return null;
        }

        var result = new HashSet<UserNotificationKind>();
        if (types.Contains(MisskeyNotificationType.Follow))
        {
            result.Add(UserNotificationKind.Follow);
        }

        if (types.Contains(MisskeyNotificationType.Renote))
        {
            result.Add(UserNotificationKind.Reblog);
        }

        if (types.Contains(MisskeyNotificationType.Reaction))
        {
            result.Add(UserNotificationKind.Favourite);
            result.Add(UserNotificationKind.Reaction);
        }

        if (types.Contains(MisskeyNotificationType.PollEnded))
        {
            result.Add(UserNotificationKind.Poll);
        }

        if (types.Contains(MisskeyNotificationType.Reply) && types.Contains(MisskeyNotificationType.Mention))
        {
            result.Add(UserNotificationKind.Mention);
        }

        if (types.Contains(MisskeyNotificationType.App))
        {
            result.Add(UserNotificationKind.Application);
            result.Add(UserNotificationKind.Update);
        }

        return result;
    }

    private static IEnumerable<UserNotificationKind> MapDomainKinds(MisskeyNotificationType type) => type switch
    {
        MisskeyNotificationType.Follow => [UserNotificationKind.Follow],
        MisskeyNotificationType.Renote => [UserNotificationKind.Reblog],
        MisskeyNotificationType.Reaction => [UserNotificationKind.Favourite, UserNotificationKind.Reaction],
        MisskeyNotificationType.PollVote or MisskeyNotificationType.PollEnded => [UserNotificationKind.Poll],
        MisskeyNotificationType.Reply or MisskeyNotificationType.Mention or MisskeyNotificationType.Quote =>
            [UserNotificationKind.Mention],
        MisskeyNotificationType.App => [UserNotificationKind.Application, UserNotificationKind.Update],
        MisskeyNotificationType.FollowRequestAccepted or
            MisskeyNotificationType.ReceiveFollowRequest or
            MisskeyNotificationType.GroupInvited => [],
        _ => []
    };

    private static string ConvertSanitizedHtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        string withLineBreaks = BreakElementRegex().Replace(html, "\n");
        return WebUtility.HtmlDecode(HtmlElementRegex().Replace(withLineBreaks, string.Empty)).Trim();
    }

    [GeneratedRegex("<(?:br\\s*/?|/p|/div|/li)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BreakElementRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex HtmlElementRegex();
}

#endif

#if !MISSKEY_BLAZOR_SERVER
public sealed class NotificationPaginationSource(
    INotificationPresentationService notifications,
    IReadOnlySet<MisskeyNotificationType>? includeTypes = null,
    IReadOnlySet<MisskeyNotificationType>? excludeTypes = null,
    bool unreadOnly = false) : IMisskeyPaginationSource<NotificationViewModel>
{
    public MisskeyPaginationOptions Options { get; } = new(10);

    public ValueTask<IReadOnlyList<NotificationViewModel>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Offset is not null || request.SinceId is not null)
        {
            throw new NotificationPresentationException("NOTIFICATION_PAGINATION_DIRECTION_UNSUPPORTED");
        }

        return new(notifications.ReadAsync(
            new(request.UntilId, request.Limit, unreadOnly, includeTypes, excludeTypes),
            cancellationToken));
    }

    public string GetId(NotificationViewModel item) => item.Id;
}
#endif

#if !MISSKEY_BLAZOR_SERVER
public sealed class NotificationPresentationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
#endif
