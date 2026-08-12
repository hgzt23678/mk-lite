using System.Text.Json.Serialization;

namespace ActivityPub.MastodonApi;

public sealed record MastodonAccount(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("acct")] string Acct,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("locked")] bool Locked,
    [property: JsonPropertyName("bot")] bool Bot,
    [property: JsonPropertyName("discoverable")] bool Discoverable,
    [property: JsonPropertyName("group")] bool Group,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("avatar")] string Avatar,
    [property: JsonPropertyName("avatar_static")] string AvatarStatic,
    [property: JsonPropertyName("header")] string Header,
    [property: JsonPropertyName("header_static")] string HeaderStatic,
    [property: JsonPropertyName("followers_count")] long FollowersCount,
    [property: JsonPropertyName("following_count")] long FollowingCount,
    [property: JsonPropertyName("statuses_count")] long StatusesCount,
    [property: JsonPropertyName("last_status_at")] string? LastStatusAt,
    [property: JsonPropertyName("emojis")] IReadOnlyList<object> Emojis,
    [property: JsonPropertyName("fields")] IReadOnlyList<object> Fields);

public sealed record MastodonStatus(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("in_reply_to_id")] string? InReplyToId,
    [property: JsonPropertyName("in_reply_to_account_id")] string? InReplyToAccountId,
    [property: JsonPropertyName("sensitive")] bool Sensitive,
    [property: JsonPropertyName("spoiler_text")] string SpoilerText,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("replies_count")] long RepliesCount,
    [property: JsonPropertyName("reblogs_count")] long ReblogsCount,
    [property: JsonPropertyName("favourites_count")] long FavouritesCount,
    [property: JsonPropertyName("favourited")] bool Favourited,
    [property: JsonPropertyName("reblogged")] bool Reblogged,
    [property: JsonPropertyName("muted")] bool Muted,
    [property: JsonPropertyName("bookmarked")] bool Bookmarked,
    [property: JsonPropertyName("pinned")] bool Pinned,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("reblog")] MastodonStatus? Reblog,
    [property: JsonPropertyName("application")] object? Application,
    [property: JsonPropertyName("account")] MastodonAccount Account,
    [property: JsonPropertyName("media_attachments")] IReadOnlyList<MastodonMediaAttachment> MediaAttachments,
    [property: JsonPropertyName("mentions")] IReadOnlyList<object> Mentions,
    [property: JsonPropertyName("tags")] IReadOnlyList<object> Tags,
    [property: JsonPropertyName("emojis")] IReadOnlyList<object> Emojis,
    [property: JsonPropertyName("card")] object? Card,
    [property: JsonPropertyName("poll")] object? Poll);

public sealed record MastodonMediaAttachment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("preview_url")] string PreviewUrl,
    [property: JsonPropertyName("remote_url")] string? RemoteUrl,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("blurhash")] string? Blurhash,
    [property: JsonPropertyName("meta")] object Meta);

public sealed record MastodonPage<T>(IReadOnlyList<T> Items, string? NextId, string? PreviousId);

public sealed record MastodonNotification(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("group_key")] string GroupKey,
    [property: JsonPropertyName("account")] MastodonAccount Account,
    [property: JsonPropertyName("status")] MastodonStatus? Status);

public sealed record MastodonRelationship(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("following")] bool Following,
    [property: JsonPropertyName("showing_reblogs")] bool ShowingReblogs,
    [property: JsonPropertyName("notifying")] bool Notifying,
    [property: JsonPropertyName("followed_by")] bool FollowedBy,
    [property: JsonPropertyName("blocking")] bool Blocking,
    [property: JsonPropertyName("blocked_by")] bool BlockedBy,
    [property: JsonPropertyName("muting")] bool Muting,
    [property: JsonPropertyName("muting_notifications")] bool MutingNotifications,
    [property: JsonPropertyName("requested")] bool Requested,
    [property: JsonPropertyName("requested_by")] bool RequestedBy,
    [property: JsonPropertyName("domain_blocking")] bool DomainBlocking,
    [property: JsonPropertyName("endorsed")] bool Endorsed,
    [property: JsonPropertyName("note")] string Note);
