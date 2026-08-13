using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Server;

public sealed class ServerMiauthAuthorizationService(
    IMisskeyAuthenticationService authentication) : IMiauthAuthorizationService
{
    public async Task AuthorizeAsync(
        string username,
        string session,
        string name,
        Uri? iconUri,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        _ = await authentication.IssueAsync(
            username,
            session,
            name,
            description: null,
            iconUri?.AbsoluteUri,
            callbackUri: null,
            permissions,
            cancellationToken).ConfigureAwait(false);
    }
}
