using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IAuthenticationFormInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachSignInAsync(
        ElementReference form,
        DotNetObjectReference<MkSignin> receiver,
        string passkeyOptionsUrl,
        string passkeyAssertionUrl,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachSignUpAsync(
        ElementReference form,
        DotNetObjectReference<MkSignup> receiver,
        string usernameAvailabilityUrl,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachInitialSetupAsync(
        ElementReference form,
        DotNetObjectReference<WelcomeSetup> receiver,
        CancellationToken cancellationToken);
}

public sealed class AuthenticationFormInterop(IJSRuntime jsRuntime) : IAuthenticationFormInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachSignInAsync(
        ElementReference form,
        DotNetObjectReference<MkSignin> receiver,
        string passkeyOptionsUrl,
        string passkeyAssertionUrl,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachSignIn",
            cancellationToken,
            form,
            receiver,
            passkeyOptionsUrl,
            passkeyAssertionUrl);
    }

    public async ValueTask<IJSObjectReference> AttachSignUpAsync(
        ElementReference form,
        DotNetObjectReference<MkSignup> receiver,
        string usernameAvailabilityUrl,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachSignUp",
            cancellationToken,
            form,
            receiver,
            usernameAvailabilityUrl);
    }

    public async ValueTask<IJSObjectReference> AttachInitialSetupAsync(
        ElementReference form,
        DotNetObjectReference<WelcomeSetup> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachInitialSetup",
            cancellationToken,
            form,
            receiver);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken) =>
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/auth-form.js",
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await module.DisposeAsync().AsTask().WaitAsync(cleanup.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
