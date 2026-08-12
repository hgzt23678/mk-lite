using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IPasswordResetFormInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachRequestAsync(
        ElementReference form,
        DotNetObjectReference<MkForgotPassword> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachCompletionAsync(
        ElementReference form,
        DotNetObjectReference<ResetPassword> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachEmailConfirmationAsync(
        ElementReference form,
        DotNetObjectReference<SignupComplete> receiver,
        CancellationToken cancellationToken);
}

public sealed class PasswordResetFormInterop(IJSRuntime jsRuntime) : IPasswordResetFormInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachRequestAsync(
        ElementReference form,
        DotNetObjectReference<MkForgotPassword> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachRequest",
            cancellationToken,
            form,
            receiver);
    }

    public async ValueTask<IJSObjectReference> AttachCompletionAsync(
        ElementReference form,
        DotNetObjectReference<ResetPassword> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachCompletion",
            cancellationToken,
            form,
            receiver);
    }

    public async ValueTask<IJSObjectReference> AttachEmailConfirmationAsync(
        ElementReference form,
        DotNetObjectReference<SignupComplete> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await GetModuleAsync(cancellationToken);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachEmailConfirmation",
            cancellationToken,
            form,
            receiver);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken) =>
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/password-reset.js",
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
