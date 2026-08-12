using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.MisskeyApi;

namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record AnnouncementPageViewModel(
    string Id,
    DateTimeOffset CreatedAt,
    string Title,
    string Text,
    string? ImageUrl,
    bool IsRead);

public interface IAnnouncementPagePresentationService
{
    Task<IReadOnlyList<AnnouncementPageViewModel>> ReadAsync(
        string? untilId,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken);
}

public sealed class AnnouncementPagePresentationService(
    MisskeyAnnouncementService announcements,
    IAuthenticatedActorContext actorContext) : IAnnouncementPagePresentationService
{
    public async Task<IReadOnlyList<AnnouncementPageViewModel>> ReadAsync(
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MisskeyAnnouncement> values = await announcements.ReadAsync(
            new(SinceId: null, untilId, limit, WithUnreads: false),
            actor.ActorIri,
            cancellationToken).ConfigureAwait(false);
        return values.Select(value => new AnnouncementPageViewModel(
            value.Id,
            value.CreatedAt,
            value.Title,
            value.Text,
            value.ImageUrl,
            value.IsRead == true)).ToArray();
    }

    public async Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        return await announcements.MarkReadAsync(id, actor.ActorIri, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class AnnouncementPaginationSource(
    IAnnouncementPagePresentationService announcements) : IMisskeyPaginationSource<AnnouncementPageViewModel>
{
    public MisskeyPaginationOptions Options { get; } = new(10);

    public ValueTask<IReadOnlyList<AnnouncementPageViewModel>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Offset is not null || request.SinceId is not null)
        {
            throw new AnnouncementPresentationException("ANNOUNCEMENT_PAGINATION_DIRECTION_UNSUPPORTED");
        }

        return new(announcements.ReadAsync(request.UntilId, request.Limit, cancellationToken));
    }

    public string GetId(AnnouncementPageViewModel item) => item.Id;
}

public sealed class AnnouncementPresentationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
