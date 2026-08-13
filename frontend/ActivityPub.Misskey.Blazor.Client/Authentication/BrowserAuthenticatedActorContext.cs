using ActivityPub.Misskey.Blazor.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class BrowserAuthenticatedActorContext(
    AuthenticationStateProvider authenticationStateProvider) : IAuthenticatedActorContext
{
    public async Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        string? username = state.User.FindFirst("preferred_username")?.Value ?? state.User.Identity?.Name;
        string? actorIri = state.User.FindFirst("activitypub.local_actor")?.Value;
        return state.User.Identity?.IsAuthenticated == true &&
               !string.IsNullOrWhiteSpace(username) &&
               !string.IsNullOrWhiteSpace(actorIri)
            ? new AuthenticatedActor(username, actorIri)
            : null;
    }

    public async Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
        await FindAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new FrontendAuthenticationException("AUTHENTICATION_REQUIRED");

    public async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        return state.User.IsInRole("admin") || state.User.IsInRole("activitypub-admin");
    }
}
