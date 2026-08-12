using System.Security.Claims;
using ActivityPub.Application;
using Microsoft.AspNetCore.Components.Authorization;

namespace ActivityPub.Misskey.Blazor.Identity;

public sealed record AuthenticatedActor(string Username, string ActorIri);

public sealed class FrontendAuthenticationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IAuthenticatedActorContext
{
    Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken);
    Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken);
    Task<bool> IsAdministratorAsync(CancellationToken cancellationToken);
}

public sealed class AuthenticatedActorContext(
    AuthenticationStateProvider authenticationStateProvider,
    IClientApiQueryService query) : IAuthenticatedActorContext
{
    public async Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken)
    {
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        ClaimsPrincipal principal = state.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? username = principal.FindFirst("preferred_username")?.Value ?? principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64 || username.Any(char.IsControl))
        {
            throw new FrontendAuthenticationException("AUTH_USERNAME_INVALID");
        }

        string? actorIri = await query.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            throw new FrontendAuthenticationException("AUTH_ACTOR_MAPPING_MISSING");
        }

        return new(username, actorIri);
    }

    public async Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
        await FindAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new FrontendAuthenticationException("AUTH_REQUIRED");

    public async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken)
    {
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        ClaimsPrincipal principal = state.User;
        return principal.IsInRole("activitypub-admin") ||
               principal.HasClaim(claim => claim.Type == "role" && claim.Value == "activitypub-admin") ||
               principal.HasClaim(claim => claim.Type == "realm_access" && claim.Value.Contains("activitypub-admin", StringComparison.Ordinal));
    }
}
