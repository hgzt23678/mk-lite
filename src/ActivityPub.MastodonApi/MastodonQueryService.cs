using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.MastodonApi;

public sealed class MastodonQueryService(
    IClientApiQueryService query,
    IClientNotificationService notifications,
    IExternalEntityIdService externalIds)
{
    public async Task<MastodonAccount?> FindAccountByLookupAsync(
        string account,
        string localDomain,
        CancellationToken cancellationToken)
    {
        ClientAccountView? value = await query.FindAccountByLookupAsync(account, localDomain, cancellationToken).ConfigureAwait(false);
        return value is null ? null : await MapAccountAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> FindLocalActorIriAsync(string username, CancellationToken cancellationToken) =>
        query.FindLocalActorIriAsync(username, cancellationToken);

    public async Task<MastodonAccount?> FindAccountByIdAsync(
        Guid id,
        string localDomain,
        CancellationToken cancellationToken)
    {
        ClientAccountView? value = await query.FindAccountByIdAsync(id, localDomain, cancellationToken).ConfigureAwait(false);
        return value is null ? null : await MapAccountAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MastodonStatus?> FindStatusAsync(
        Guid id,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ClientPostView? value = await query.FindPostAsync(id, viewerActorIri, cancellationToken).ConfigureAwait(false);
        return value is null ? null : await MapStatusAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MastodonStatus?> FindStreamStatusAsync(
        Guid id,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        ClientPostView? value = await query.FindStreamPostAsync(
            id,
            viewerActorIri,
            audience,
            localOnly,
            cancellationToken).ConfigureAwait(false);
        return value is null ? null : await MapStatusAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CanReceiveStreamEventAsync(
        StreamEvent streamEvent,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken) =>
        query.CanReceiveStreamEventAsync(streamEvent, viewerActorIri, audience, localOnly, cancellationToken);

    public Task<string> MapStreamPostIdAsync(
        Guid id,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            id,
            occurredAt,
            cancellationToken);

    public async Task<MastodonPage<MastodonNotification>> ReadNotificationsAsync(
        string recipientActorIri,
        Guid? beforeId,
        int limit,
        IReadOnlySet<UserNotificationKind>? includeKinds,
        IReadOnlySet<UserNotificationKind>? excludeKinds,
        CancellationToken cancellationToken)
    {
        var effectiveExcludedKinds = excludeKinds is null
            ? new HashSet<UserNotificationKind>()
            : new HashSet<UserNotificationKind>(excludeKinds);
        effectiveExcludedKinds.Add(UserNotificationKind.Reaction);
        ClientPage<ClientNotificationView> page = await notifications.ReadAsync(
            recipientActorIri,
            new(beforeId, limit, UnreadOnly: false, MarkAsRead: false, includeKinds, effectiveExcludedKinds),
            cancellationToken).ConfigureAwait(false);
        var items = new List<MastodonNotification>(page.Items.Count);
        foreach (ClientNotificationView item in page.Items)
        {
            items.Add(await MapNotificationAsync(item, cancellationToken).ConfigureAwait(false));
        }

        return new(
            items,
            await MapNotificationCursorAsync(page.Next, cancellationToken).ConfigureAwait(false),
            await MapNotificationCursorAsync(page.Previous, cancellationToken).ConfigureAwait(false));
    }

    public async Task<MastodonNotification?> FindNotificationAsync(
        string recipientActorIri,
        Guid id,
        CancellationToken cancellationToken)
    {
        ClientNotificationView? item = await notifications.FindAsync(recipientActorIri, id, cancellationToken).ConfigureAwait(false);
        return item is null || item.Kind == UserNotificationKind.Reaction
            ? null
            : await MapNotificationAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DismissNotificationAsync(
        string recipientActorIri,
        Guid id,
        CancellationToken cancellationToken) =>
        notifications.DismissAsync(recipientActorIri, id, DateTimeOffset.UtcNow, cancellationToken);

    public Task<int> ClearNotificationsAsync(string recipientActorIri, CancellationToken cancellationToken) =>
        notifications.ClearAsync(recipientActorIri, DateTimeOffset.UtcNow, cancellationToken);

    public Task<long> CountUnreadNotificationsAsync(string recipientActorIri, CancellationToken cancellationToken) =>
        notifications.CountUnreadAsync(recipientActorIri, cancellationToken);

    private async Task<MastodonNotification> MapNotificationAsync(
        ClientNotificationView value,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Notification,
            value.Id,
            value.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        return new(
            id,
            value.Kind switch
            {
                UserNotificationKind.Follow => "follow",
                UserNotificationKind.Favourite => "favourite",
                UserNotificationKind.Reblog => "reblog",
                UserNotificationKind.Poll => "poll",
                UserNotificationKind.Update => "update",
                _ => "mention"
            },
            value.CreatedAt,
            "ungrouped-" + id,
            await MapAccountAsync(value.Account, cancellationToken).ConfigureAwait(false),
            value.Post is null ? null : await MapStatusAsync(value.Post, cancellationToken).ConfigureAwait(false));
    }

    private Task<string?> MapNotificationCursorAsync(ClientPageCursor? cursor, CancellationToken cancellationToken) =>
        cursor is null
            ? Task.FromResult<string?>(null)
            : MapNotificationCursorCoreAsync(cursor, cancellationToken);

    private async Task<string?> MapNotificationCursorCoreAsync(
        ClientPageCursor cursor,
        CancellationToken cancellationToken) =>
        await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Notification,
            cursor.Id,
            cursor.Timestamp,
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonPage<MastodonStatus>> ReadPublicTimelineAsync(
        Guid? maxId,
        int limit,
        bool localOnly,
        CancellationToken cancellationToken) =>
        await MapPageAsync(
            await query.ReadPublicTimelineAsync(maxId, limit, localOnly, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonPage<MastodonStatus>> ReadAccountStatusesAsync(
        Guid accountId,
        string localDomain,
        Guid? maxId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken) =>
        await MapPageAsync(
            await query.ReadAccountPostsAsync(
                accountId,
                localDomain,
                maxId,
                limit,
                viewerActorIri,
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonPage<MastodonStatus>> ReadHomeTimelineAsync(
        string viewerActorIri,
        Guid? maxId,
        int limit,
        CancellationToken cancellationToken) =>
        await MapPageAsync(
            await query.ReadHomeTimelineAsync(viewerActorIri, maxId, limit, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private async Task<MastodonPage<MastodonStatus>> MapPageAsync(
        ClientPage<ClientPostView> page,
        CancellationToken cancellationToken)
    {
        var items = new List<MastodonStatus>(page.Items.Count);
        foreach (ClientPostView item in page.Items)
        {
            items.Add(await MapStatusAsync(item, cancellationToken).ConfigureAwait(false));
        }

        return new(
            items,
            await MapCursorAsync(page.Next, cancellationToken).ConfigureAwait(false),
            await MapCursorAsync(page.Previous, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string?> MapCursorAsync(ClientPageCursor? cursor, CancellationToken cancellationToken) =>
        cursor is null
            ? null
            : await externalIds.GetOrCreateAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Post,
                cursor.Id,
                cursor.Timestamp,
                cancellationToken).ConfigureAwait(false);

    private async Task<MastodonAccount> MapAccountAsync(
        ClientAccountView value,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            value.Id,
            value.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        return new(
            id,
            value.Username,
            value.Acct,
            value.DisplayName,
            value.Locked,
            value.Bot,
            value.Discoverable,
            value.Group,
            value.CreatedAt,
            value.SummaryHtml,
            value.Url,
            value.Iri,
            value.AvatarUrl,
            value.AvatarUrl,
            value.HeaderUrl,
            value.HeaderUrl,
            value.FollowersCount,
            value.FollowingCount,
            value.PostsCount,
            value.LastPostAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            value.Emojis.Select(emoji => (object)new
            {
                shortcode = emoji.Shortcode,
                url = emoji.Url,
                static_url = emoji.StaticUrl,
                visible_in_picker = emoji.VisibleInPicker,
                category = emoji.Category
            }).ToArray(),
            value.Fields.Select(field => (object)new
            {
                name = field.Name,
                value = field.Value,
                verified_at = field.VerifiedAt
            }).ToArray());
    }

    private async Task<MastodonStatus> MapStatusAsync(
        ClientPostView value,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            value.Id,
            value.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        MastodonAccount account = await MapAccountAsync(value.Account, cancellationToken).ConfigureAwait(false);
        var attachments = new List<MastodonMediaAttachment>(value.Attachments.Count);
        foreach (ClientMediaView attachment in value.Attachments)
        {
            string mediaId = await externalIds.GetOrCreateAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Media,
                attachment.Id,
                attachment.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            attachments.Add(new(
                mediaId,
                MapMediaType(attachment.MediaType),
                attachment.Url,
                attachment.PreviewUrl,
                attachment.RemoteUrl,
                attachment.Description,
                attachment.Blurhash,
                new
                {
                    original = new
                    {
                        width = attachment.Width,
                        height = attachment.Height,
                        size = attachment.Width is not null && attachment.Height is not null
                            ? $"{attachment.Width}x{attachment.Height}"
                            : null
                    }
                }));
        }

        string? replyId = value.InReplyToId is null
            ? null
            : await externalIds.GetOrCreateAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Post,
                value.InReplyToId.Value,
                value.CreatedAt,
                cancellationToken).ConfigureAwait(false);
        string? replyAccountId = value.InReplyToAccountId is null
            ? null
            : await externalIds.GetOrCreateAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Actor,
                value.InReplyToAccountId.Value,
                value.CreatedAt,
                cancellationToken).ConfigureAwait(false);
        MastodonStatus? reblog = value.AnnouncedPost is null
            ? null
            : await MapStatusAsync(value.AnnouncedPost, cancellationToken).ConfigureAwait(false);
        var mentions = new List<object>(value.Mentions.Count);
        foreach (ClientMentionView mention in value.Mentions)
        {
            mentions.Add(new
            {
                id = await MapAccountIdAsync(mention.AccountId, value.CreatedAt, cancellationToken).ConfigureAwait(false),
                username = mention.Username,
                acct = mention.Acct,
                url = mention.Iri
            });
        }

        return new(
            id,
            value.CreatedAt,
            replyId,
            replyAccountId,
            value.Sensitive,
            value.ContentWarning,
            MapVisibility(value.Visibility),
            value.Language,
            value.Iri,
            value.Url,
            value.RepliesCount,
            value.AnnouncesCount,
            value.LikesCount,
            value.LikedByViewer,
            value.AnnouncedByViewer,
            value.MutedForViewer,
            value.BookmarkedByViewer,
            value.PinnedForViewer,
            value.SanitizedHtml,
            reblog,
            null,
            account,
            attachments,
            mentions,
            value.Hashtags.Select(hashtag => (object)new { name = hashtag.Name, url = hashtag.Url }).ToArray(),
            value.Emojis.Select(emoji => (object)new
            {
                shortcode = emoji.Shortcode,
                url = emoji.Url,
                static_url = emoji.StaticUrl,
                visible_in_picker = emoji.VisibleInPicker,
                category = emoji.Category
            }).ToArray(),
            null,
            value.Poll is null ? null : await MapPollAsync(value.Poll, cancellationToken).ConfigureAwait(false));
    }

    private Task<string> MapAccountIdAsync(Guid id, DateTimeOffset timestamp, CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(ApiDialect.Mastodon, ExternalEntityType.Actor, id, timestamp, cancellationToken);

    private async Task<object> MapPollAsync(ClientPollView poll, CancellationToken cancellationToken) => new
    {
        id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Poll,
            poll.Id,
            poll.ExpiresAt ?? DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false),
        expires_at = poll.ExpiresAt,
        expired = poll.Expired,
        multiple = poll.Multiple,
        votes_count = poll.VotesCount,
        voters_count = poll.VotersCount,
        voted = poll.VotedByViewer,
        own_votes = poll.OwnVotes,
        options = poll.Options.Select(option => new { title = option.Title, votes_count = option.VotesCount }),
        emojis = Array.Empty<object>()
    };

    private static string MapVisibility(Visibility visibility) => visibility switch
    {
        Visibility.Public => "public",
        Visibility.Unlisted => "unlisted",
        Visibility.FollowersOnly => "private",
        Visibility.MentionedOnly => "direct",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility))
    };

    private static string MapMediaType(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        string value when value.Contains("image", StringComparison.Ordinal) => "image",
        string value when value.Contains("video", StringComparison.Ordinal) => "video",
        string value when value.Contains("audio", StringComparison.Ordinal) => "audio",
        _ => "unknown"
    };
}
