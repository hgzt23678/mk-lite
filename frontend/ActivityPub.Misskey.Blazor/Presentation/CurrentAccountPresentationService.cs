#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.Identity;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public interface ICurrentAccountPresentationService
{
    Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed class CurrentAccountPresentationService(
    IClientApiQueryService query,
    IAuthenticatedActorContext actorContext) : ICurrentAccountPresentationService
{
    public async Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientAccountView account = await query.FindAccountByIriAsync(actor.ActorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new FrontendAuthenticationException("AUTH_ACTOR_MAPPING_MISSING");
        return new NoteAuthorViewModel(
            account.Id.ToString("N"),
            account.Username,
            account.Acct,
            string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            string.IsNullOrWhiteSpace(account.AvatarUrl) ? "/static-assets/user-unknown.png" : account.AvatarUrl,
            account.Bot);
    }
}
#endif
