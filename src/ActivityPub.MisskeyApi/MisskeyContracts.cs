namespace ActivityPub.MisskeyApi;

using System.Text.Json.Serialization;

public sealed record MisskeyReactionRequest(string NoteId, string? Reaction);

public sealed record MisskeyCreateNoteRequest(
    string? Text,
    string? Visibility,
    IReadOnlyList<string>? VisibleUserIds,
    string? Cw,
    bool LocalOnly,
    IReadOnlyList<string>? FileIds,
    string? ReplyId,
    string? RenoteId,
    string? ChannelId,
    MisskeyPollRequest? Poll);

public sealed record MisskeyPollRequest(
    IReadOnlyList<string>? Choices,
    bool Multiple,
    long? ExpiresAt,
    long? ExpiredAfter);

public sealed record MisskeyDeleteNoteRequest(string NoteId);

public sealed record MisskeyPollVoteRequest(string NoteId, int Choice);

public sealed record MisskeyApiErrorBody(MisskeyApiError Error);

public sealed record MisskeyApiError(string Message, string Code, string Id, string Kind);

public sealed record MisskeyFederationInstancesRequest(
    string? Host,
    bool? Blocked,
    bool? NotResponding,
    bool? Suspended,
    bool? Federating,
    bool? Subscribing,
    bool? Publishing,
    int Limit,
    int Offset,
    string? Sort);

public sealed record MisskeyUserFollowRelationsRequest(
    string? UserId,
    string? Username,
    string? Host,
    string? SinceId,
    string? UntilId,
    int Limit);

public sealed record MisskeyFederationInstance(
    string Id,
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

public sealed record MisskeyAnnouncementQuery(
    string? SinceId,
    string? UntilId,
    int Limit,
    bool WithUnreads);

public sealed record MisskeyAnnouncement(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Text,
    string Title,
    string? ImageUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsRead);

public sealed record MisskeyAdminAnnouncement(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Text,
    string Title,
    string? ImageUrl,
    long Reads);

public sealed record MisskeyAnnouncementMutation(string Title, string Text, string? ImageUrl);

public sealed class MisskeyApiException(
    int statusCode,
    string message,
    string code,
    string id,
    string kind = "client") : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public MisskeyApiErrorBody Body { get; } = new(new(message, code, id, kind));
}
