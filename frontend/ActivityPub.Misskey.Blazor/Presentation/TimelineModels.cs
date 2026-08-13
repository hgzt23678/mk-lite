namespace ActivityPub.Misskey.Blazor.Presentation;

public enum Visibility
{
    Public,
    Unlisted,
    FollowersOnly,
    MentionedOnly
}

public enum TimelineKind
{
    Home,
    Local,
    Global,
    Hybrid
}

public sealed record TimelinePageViewModel(
    IReadOnlyList<NoteViewModel> Notes,
    string? NextCursor);

public sealed record NoteViewModel(
    Guid InternalId,
    string Id,
    DateTimeOffset CreatedAt,
    NoteAuthorViewModel Author,
    string Text,
    string? ContentWarning,
    Visibility Visibility,
    string? ReplyId,
    long RepliesCount,
    long RenotesCount,
    long ReactionsCount,
    bool ReactedByViewer,
    IReadOnlyDictionary<string, long> Reactions,
    string? ViewerReaction,
    IReadOnlyList<NoteMediaViewModel> Media,
    IReadOnlyList<string> Mentions,
    IReadOnlyList<string> Hashtags,
    IReadOnlyDictionary<string, string> Emojis,
    NotePollViewModel? Poll,
    NoteViewModel? Renote,
    bool LocalOnly = false,
    IReadOnlyList<string>? VisibleUserIds = null,
    bool IsHidden = false,
    DateTimeOffset? DeletedAt = null,
    string? RenoteId = null,
    NoteViewModel? Reply = null,
    bool ShouldInsertAdvertisement = false,
    bool IsMuted = false,
    string? RemoteUrl = null);

public sealed record NoteAuthorViewModel(
    string Id,
    string Username,
    string Acct,
    string DisplayName,
    string AvatarUrl,
    bool IsBot,
    bool IsCat = false,
    string? AvatarBlurhash = null,
    string OnlineStatus = "unknown",
    IReadOnlyDictionary<string, string>? Emojis = null);

public sealed record NoteMediaViewModel(
    string Id,
    string MediaType,
    string Url,
    string PreviewUrl,
    string? Description,
    string? Blurhash,
    int? Width,
    int? Height,
    bool Sensitive,
    long? Size = null);

public sealed record NotePollViewModel(
    string Id,
    DateTimeOffset? ExpiresAt,
    bool Expired,
    bool Multiple,
    bool? VotedByViewer,
    IReadOnlyList<int> OwnVotes,
    IReadOnlyList<NotePollOptionViewModel> Options);

public sealed record NotePollOptionViewModel(string Title, long VotesCount);

public sealed record NoteDraft(
    string Text,
    string? ContentWarning,
    Visibility Visibility,
    Guid? ReplyToId,
    Guid? QuoteTargetId,
    IReadOnlyList<Guid> MediaIds,
    bool Sensitive = false,
    NotePollDraft? Poll = null);

public sealed record NotePollDraft(
    IReadOnlyList<string> Choices,
    bool Multiple,
    DateTimeOffset? ExpiresAt);

public enum TimelineMutationKind
{
    Checkpoint,
    Upsert,
    Remove
}

public sealed record TimelineMutation(
    long Cursor,
    TimelineMutationKind Kind,
    string NoteId,
    NoteViewModel? Note);

public sealed class TimelineCursorException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public enum FrontendPollVoteError
{
    NoPoll,
    InvalidChoice,
    AlreadyVoted,
    Expired,
    Blocked,
    NotVisible
}

public sealed class FrontendPollVoteException(FrontendPollVoteError error, string message)
    : InvalidOperationException(message)
{
    public FrontendPollVoteError Error { get; } = error;
}
