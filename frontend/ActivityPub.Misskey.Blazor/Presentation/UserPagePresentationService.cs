namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed class UserPagePresentationService(
    IUserPreviewPresentationService users,
    TimelinePresentationService timeline) : IUserPagePresentationService
{
    public async Task<UserPageViewModel> ReadAsync(
        string acct,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        UserPreviewViewModel user = await users.ReadAsync(
            acct,
            cancellationToken).ConfigureAwait(false);
        TimelinePageViewModel notes = await timeline.ReadUserNotesAsync(
            user,
            untilId,
            limit,
            cancellationToken).ConfigureAwait(false);
        return new(user, notes);
    }
}
