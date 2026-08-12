using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed record ClientAccountView(
    Guid Id,
    string Username,
    string Acct,
    string DisplayName,
    bool Locked,
    bool Bot,
    bool Discoverable,
    bool Group,
    DateTimeOffset CreatedAt,
    string SummaryHtml,
    string Url,
    string Iri,
    string AvatarUrl,
    string HeaderUrl,
    long FollowersCount,
    long FollowingCount,
    long PostsCount,
    DateTimeOffset? LastPostAt,
    IReadOnlyList<ClientCustomEmojiView> Emojis,
    IReadOnlyList<ClientProfileFieldView> Fields);

public sealed record ClientProfileFieldView(string Name, string Value, DateTimeOffset? VerifiedAt);

public sealed record ClientCustomEmojiView(
    string Shortcode,
    string Url,
    string StaticUrl,
    bool VisibleInPicker,
    string? Category);

public sealed record ClientMediaView(
    Guid Id,
    DateTimeOffset CreatedAt,
    string MediaType,
    string Url,
    string PreviewUrl,
    string? RemoteUrl,
    string? Description,
    string? Blurhash,
    int? Width,
    int? Height,
    long? Size);

public sealed record ClientPostView(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? InReplyToId,
    Guid? InReplyToAccountId,
    bool Sensitive,
    string ContentWarning,
    Visibility Visibility,
    string? Language,
    string Iri,
    string Url,
    long RepliesCount,
    long AnnouncesCount,
    long LikesCount,
    bool LikedByViewer,
    bool AnnouncedByViewer,
    bool MutedForViewer,
    bool BookmarkedByViewer,
    bool PinnedForViewer,
    string SanitizedHtml,
    string? SourceText,
    string? SourceFormat,
    ClientPostView? AnnouncedPost,
    ClientAccountView Account,
    IReadOnlyList<ClientMediaView> Attachments,
    IReadOnlyList<ClientMentionView> Mentions,
    IReadOnlyList<ClientHashtagView> Hashtags,
    IReadOnlyList<ClientCustomEmojiView> Emojis,
    ClientPollView? Poll,
    bool LocalOnly = false,
    IReadOnlyList<ClientAccountView>? VisibleRecipients = null);

public sealed record ClientReactionSummaryView(
    IReadOnlyDictionary<string, long> Reactions,
    string? ViewerReaction,
    IReadOnlyDictionary<string, string> CustomEmojiUrls);

public sealed record ClientReactionActorView(
    Guid RelationId,
    DateTimeOffset CreatedAt,
    string ActorIri);

public sealed record ClientAnnounceActorView(
    Guid RelationId,
    DateTimeOffset CreatedAt,
    string ActorIri);

public sealed record ClientMentionView(Guid AccountId, string Username, string Acct, string Iri);

public sealed record ClientHashtagView(string Name, string Url);

public sealed record ClientPollView(
    Guid Id,
    DateTimeOffset? ExpiresAt,
    bool Expired,
    bool Multiple,
    long VotesCount,
    long VotersCount,
    bool? VotedByViewer,
    IReadOnlyList<int> OwnVotes,
    IReadOnlyList<ClientPollOptionView> Options);

public sealed record ClientPollOptionView(string Title, long VotesCount);

public sealed record ClientPageCursor(Guid Id, DateTimeOffset Timestamp);

public sealed record ClientPage<T>(
    IReadOnlyList<T> Items,
    ClientPageCursor? Next,
    ClientPageCursor? Previous);

public enum ClientStreamAudience
{
    Public,
    Home
}

public sealed record ClientRelationshipView(
    Guid AccountId,
    bool Following,
    bool ShowingAnnounces,
    bool Notifying,
    bool FollowedBy,
    bool Blocking,
    bool BlockedBy,
    bool Muting,
    bool MutingNotifications,
    bool Requested,
    bool RequestedBy,
    bool DomainBlocking,
    bool Endorsed,
    string Note);

public sealed record ClientFollowRelationView(
    Guid Id,
    DateTimeOffset CreatedAt,
    ClientAccountView Follower,
    ClientAccountView Followee);

public sealed record ClientNotificationView(
    Guid Id,
    UserNotificationKind Kind,
    DateTimeOffset CreatedAt,
    bool IsRead,
    string? Reaction,
    ClientAccountView Account,
    ClientPostView? Post);

public sealed record ClientNotificationQuery(
    Guid? BeforeId,
    int Limit,
    bool UnreadOnly,
    bool MarkAsRead,
    IReadOnlySet<UserNotificationKind>? IncludeKinds,
    IReadOnlySet<UserNotificationKind>? ExcludeKinds);

public enum RemoteInstanceSortField
{
    Created,
    Notes,
    Users,
    Following,
    Followers,
    LastCommunicated
}

public sealed record RemoteInstanceQuery(
    string? Host,
    bool? Blocked,
    bool? NotResponding,
    bool? Suspended,
    bool? Federating,
    bool? Subscribing,
    bool? Publishing,
    RemoteInstanceSortField Sort,
    bool Descending,
    int Offset,
    int Limit);

public sealed record RemoteInstanceView(
    Guid Id,
    DateTimeOffset CaughtAt,
    string Host,
    long UsersCount,
    long NotesCount,
    long FollowingCount,
    long FollowersCount,
    DateTimeOffset? LatestRequestSentAt,
    DateTimeOffset LastCommunicatedAt,
    bool IsNotResponding,
    bool IsSuspended,
    bool IsBlocked,
    string? SoftwareName,
    string? SoftwareVersion,
    bool? OpenRegistrations,
    string? Name,
    string? Description,
    string? MaintainerName,
    string? MaintainerEmail,
    string? IconUrl,
    string? FaviconUrl,
    string? ThemeColor,
    DateTimeOffset? InfoUpdatedAt);

public interface IRemoteInstanceQueryService
{
    Task<IReadOnlyList<RemoteInstanceView>> ReadAsync(
        RemoteInstanceQuery query,
        CancellationToken cancellationToken);
}

public interface IClientNotificationService
{
    Task<ClientPage<ClientNotificationView>> ReadAsync(
        string recipientActorIri,
        ClientNotificationQuery query,
        CancellationToken cancellationToken);

    Task<ClientNotificationView?> FindAsync(
        string recipientActorIri,
        Guid id,
        CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(
        string recipientActorIri,
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> DismissAsync(string recipientActorIri, Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> ClearAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken);
    Task<long> CountUnreadAsync(string recipientActorIri, CancellationToken cancellationToken);
}

public sealed record ClientPostMutation(
    string SourceText,
    string SourceFormat,
    Visibility Visibility,
    string? ContentWarning,
    bool Sensitive,
    Guid? InReplyToId,
    Guid? QuoteTargetId,
    IReadOnlyList<Guid> MediaIds,
    ClientPollMutation? Poll = null);

public sealed record ClientPollMutation(
    IReadOnlyList<string> Choices,
    bool Multiple,
    DateTimeOffset? ExpiresAt);

public interface IClientApiQueryService
{
    Task<ClientReactionSummaryView> ReadPostReactionsAsync(
        Guid postId,
        string? viewerActorIri,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientReactionActorView>> ReadPostReactionActorsAsync(
        Guid postId,
        string reaction,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientAnnounceActorView>> ReadPostAnnounceActorsAsync(
        Guid postId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken);

    Task<ClientAccountView?> FindAccountByLookupAsync(
        string account,
        string localDomain,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientAccountView>> SearchAccountsByUsernameAsync(
        string username,
        string? host,
        int limit,
        string localDomain,
        CancellationToken cancellationToken);

    Task<ClientAccountView?> FindAccountByIdAsync(
        Guid id,
        string localDomain,
        CancellationToken cancellationToken);

    Task<ClientAccountView?> FindAccountByIriAsync(
        string actorIri,
        CancellationToken cancellationToken);

    Task<string?> FindLocalActorIriAsync(string username, CancellationToken cancellationToken);

    Task<ClientPostView?> FindPostAsync(
        Guid id,
        string? viewerActorIri,
        CancellationToken cancellationToken);

    Task<ClientPostView?> FindStreamPostAsync(
        Guid id,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken);

    Task<bool> CanReceiveStreamEventAsync(
        StreamEvent streamEvent,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken);

    Task<ClientPage<ClientPostView>> ReadPublicTimelineAsync(
        Guid? beforeId,
        int limit,
        bool localOnly,
        CancellationToken cancellationToken);

    Task<ClientPage<ClientPostView>> ReadAccountPostsAsync(
        Guid accountId,
        string localDomain,
        Guid? beforeId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken);

    Task<ClientPage<ClientPostView>> ReadHomeTimelineAsync(
        string viewerActorIri,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView?> FindRelationshipAsync(
        string ownerActorIri,
        Guid accountId,
        string localDomain,
        CancellationToken cancellationToken);

    Task<ClientPage<ClientFollowRelationView>> ReadFollowRelationsAsync(
        Guid accountId,
        bool followers,
        Guid? beforeId,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken);
}

public interface IClientApiCommandService
{
    Task<ClientPostView> CreatePostAsync(
        string username,
        string idempotencyKey,
        ClientPostMutation mutation,
        CancellationToken cancellationToken);

    Task<ClientPostView> DeletePostAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> LikeAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> UndoLikeAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> ReactAsync(
        string username,
        Guid postId,
        string reaction,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> UndoReactionAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> VotePollAsync(
        string username,
        Guid postId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> AnnounceAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientPostView> UndoAnnounceAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> FollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> UnfollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> MuteAsync(
        string username,
        Guid accountId,
        bool hideNotifications,
        TimeSpan? duration,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> UnmuteAsync(
        string username,
        Guid accountId,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> BlockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientRelationshipView> UnblockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public enum ClientReactionStateError
{
    AlreadyReacted,
    NotReacted
}

public sealed class ClientReactionStateException(ClientReactionStateError error, string message) : InvalidOperationException(message)
{
    public ClientReactionStateError Error { get; } = error;
}

public enum ClientPollVoteError
{
    NoPoll,
    InvalidChoice,
    AlreadyVoted,
    Expired,
    Blocked,
    NotVisible
}

public sealed class ClientPollVoteException(ClientPollVoteError error, string message) : InvalidOperationException(message)
{
    public ClientPollVoteError Error { get; } = error;
}
