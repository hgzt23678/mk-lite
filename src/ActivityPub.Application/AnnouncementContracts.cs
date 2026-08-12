using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed record AnnouncementView(
    Guid Id,
    long SortOrdinal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Title,
    string Text,
    string? ImageUrl,
    AnnouncementAudience Audience,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    bool? IsRead,
    long Reads);

public sealed record AnnouncementQuery(
    Guid? SinceId,
    Guid? UntilId,
    int Limit,
    bool WithUnreads,
    string? ViewerActorIri);

public sealed record AnnouncementMutation(
    string Title,
    string Text,
    string? ImageUrl,
    AnnouncementAudience? Audience,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    bool ReplaceExpiresAt = false);

public enum AnnouncementImageImportFailure
{
    InvalidSource,
    RejectedByPolicy,
    MediaUnavailable,
    RemoteFetchFailed,
    ProcessingFailed
}

public sealed class AnnouncementImageImportException : Exception
{
    public AnnouncementImageImportException(
        AnnouncementImageImportFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    public AnnouncementImageImportFailure Failure { get; }
}

public interface IAnnouncementImageImporter
{
    Task<string?> ImportAsync(
        string? sourceImageUrl,
        string ownerActorIri,
        CancellationToken cancellationToken);
}

public interface IAnnouncementService
{
    Task<IReadOnlyList<AnnouncementView>> ReadAsync(
        AnnouncementQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnnouncementView>> ReadForAdministrationAsync(
        Guid? sinceId,
        Guid? untilId,
        int limit,
        CancellationToken cancellationToken);

    Task<AnnouncementView> CreateAsync(
        AnnouncementMutation mutation,
        string operatorId,
        CancellationToken cancellationToken);

    Task<AnnouncementView?> UpdateAsync(
        Guid id,
        AnnouncementMutation mutation,
        string operatorId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid id,
        string operatorId,
        CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(
        Guid id,
        string readerActorIri,
        CancellationToken cancellationToken);
}
