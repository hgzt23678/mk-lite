using ActivityPub.MisskeyApi;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IAnnouncementPresentationService
{
    Task<IReadOnlyList<VisitorAnnouncementViewModel>> ReadPublicAsync(
        int limit,
        CancellationToken cancellationToken);
}

public sealed class AnnouncementPresentationService(
    MisskeyAnnouncementService announcements) : IAnnouncementPresentationService
{
    public async Task<IReadOnlyList<VisitorAnnouncementViewModel>> ReadPublicAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MisskeyAnnouncement> values = await announcements.ReadAsync(
            new(SinceId: null, UntilId: null, limit, WithUnreads: false),
            viewerActorIri: null,
            cancellationToken).ConfigureAwait(false);
        return values.Select(value => new VisitorAnnouncementViewModel(
            value.Id,
            value.Title,
            value.Text,
            value.ImageUrl)).ToArray();
    }
}
