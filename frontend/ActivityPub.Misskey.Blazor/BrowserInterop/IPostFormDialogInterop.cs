using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IPostFormDialogInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference modal,
        ElementReference content,
        ElementReference initialFocus,
        DotNetObjectReference<MkPostFormDialog> receiver,
        CancellationToken cancellationToken);
}

public sealed class PostFormDialogInterop(IJSRuntime jsRuntime) : IPostFormDialogInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference modal,
        ElementReference content,
        ElementReference initialFocus,
        DotNetObjectReference<MkPostFormDialog> receiver,
        CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/post-form-dialog.js",
            cancellationToken);
        return await module.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            modal,
            content,
            initialFocus,
            receiver);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
