namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed class UserPagePaginationSource(
    IUserPagePresentationService service,
    UserPreviewViewModel user,
    TimelinePageViewModel seed) : IMisskeyPaginationSource<NoteViewModel>
{
    private bool useSeed = true;

    public MisskeyPaginationOptions Options { get; } = new(10);

    public async ValueTask<IReadOnlyList<NoteViewModel>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        if (useSeed && request.UntilId is null)
        {
            useSeed = false;
            return seed.Notes;
        }

        UserPageViewModel result = await service.ReadAsync(
            user.User.Acct,
            request.UntilId,
            request.Limit,
            cancellationToken).ConfigureAwait(false);
        return result.Notes.Notes;
    }

    public string GetId(NoteViewModel item) => item.Id;
}
