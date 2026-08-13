using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class FrontendSessionAuthenticationStateProvider(
    FrontendSessionClient sessions) : AuthenticationStateProvider
{
    private const string AuthenticationType = "FrontendSessionCookie";
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));
    private Task<AuthenticationState>? currentState;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        currentState ??= LoadAsync(CancellationToken.None);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        currentState ??= LoadAsync(cancellationToken);
        await currentState.ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Task<AuthenticationState> next = LoadAsync(cancellationToken);
        currentState = next;
        AuthenticationState state = await next.ConfigureAwait(false);
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }

    private async Task<AuthenticationState> LoadAsync(CancellationToken cancellationToken)
    {
        FrontendSessionSnapshot snapshot = await sessions.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Authenticated || snapshot.Viewer is not FrontendViewer viewer)
        {
            return Anonymous;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, viewer.ActorIri),
            new(ClaimTypes.Name, viewer.Username),
            new("preferred_username", viewer.Username),
            new("activitypub.local_actor", viewer.ActorIri)
        };
        claims.AddRange(viewer.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
    }
}
