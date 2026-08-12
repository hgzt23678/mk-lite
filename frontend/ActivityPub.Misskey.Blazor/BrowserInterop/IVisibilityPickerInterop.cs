using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IVisibilityPickerInterop : IAsyncDisposable
{
    ValueTask<ModalAttachment> AttachAsync(
        ElementReference source,
        ElementReference modal,
        ElementReference content,
        DotNetObjectReference<MkVisibilityPicker> receiver,
        CancellationToken cancellationToken);
}

public sealed class VisibilityPickerInterop(IJSRuntime jsRuntime) : IVisibilityPickerInterop
{
    private IJSObjectReference? module;

    public async ValueTask<ModalAttachment> AttachAsync(
        ElementReference source,
        ElementReference modal,
        ElementReference content,
        DotNetObjectReference<MkVisibilityPicker> receiver,
        CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/modal.js",
            cancellationToken);
        IJSObjectReference handle = await module.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            source,
            modal,
            content,
            false,
            receiver,
            "high");
        ModalPlacement placement = await handle.InvokeAsync<ModalPlacement>("getPlacement", cancellationToken);
        return new ModalAttachment(
            handle,
            placement.IsDrawer,
            placement.MaximumHeight,
            placement.TransformOrigin,
            placement.SourceWidth);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
