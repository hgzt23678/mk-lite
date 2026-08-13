using System.Security.Cryptography;
using System.Text;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Client.Authentication;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Microsoft.AspNetCore.Components.Authorization;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed class BrowserMisskeyAccountState(
    BrowserCurrentAccountPresentationService currentAccount,
    FrontendSessionAuthenticationStateProvider authentication) : IMisskeyAccountState
{
    private MisskeyAccountSnapshot? current;

    public MisskeyAccountSnapshot? Current => current;

    public bool IsAdministrator => current?.IsAdmin == true;

    public bool IsModerator => current?.IsModerator == true;

    public async Task<MisskeyAccountSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        AuthenticationState state = await authentication.GetAuthenticationStateAsync().ConfigureAwait(false);
        if (state.User.Identity?.IsAuthenticated != true)
        {
            current = null;
            return null;
        }

        System.Text.Json.JsonElement document = await currentAccount.GetDocumentAsync(cancellationToken)
            .ConfigureAwait(false);
        NoteAuthorViewModel account = BrowserTimelinePresentationService.MapAuthor(document);
        string actorIri = state.User.FindFirst("activitypub.local_actor")?.Value
            ?? throw new InvalidOperationException("The authenticated browser session omitted its actor identifier.");
        current = new(
            StableGuid(account.Id),
            account.Username,
            account.Acct,
            account.DisplayName,
            state.User.IsInRole("admin") || state.User.IsInRole("activitypub-admin"),
            state.User.IsInRole("moderator") || state.User.IsInRole("activitypub-moderator"),
            document.OptionalBoolean("isLocked"),
            document.OptionalBoolean("hasUnreadNotification"),
            document.OptionalBoolean("hasUnreadMessagingMessage"),
            document.OptionalBoolean("hasUnreadAnnouncement"),
            document.OptionalBoolean("hasPendingReceivedFollowRequest"),
            account.AvatarUrl,
            actorIri);
        return current;
    }

    public Task AddAccountAsync(Guid id, string token, CancellationToken cancellationToken = default)
    {
        _ = id;
        _ = token;
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("HttpOnly browser sessions cannot import a readable account token.");
    }

    public Task RemoveAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = id;
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("HttpOnly browser sessions must be removed through the logout endpoint.");
    }

    public ValueTask<IReadOnlyList<MisskeyStoredAccount>> ReadStoredAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<MisskeyStoredAccount>>([]);
    }

    private static Guid StableGuid(string value)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(digest.AsSpan(0, 16));
    }
}
