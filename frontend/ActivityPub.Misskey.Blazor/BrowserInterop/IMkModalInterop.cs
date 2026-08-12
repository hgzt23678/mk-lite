using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record MkModalInteropOptions(
    string PreferType,
    string AnchorX,
    string AnchorY,
    string Priority,
    bool NoOverlap,
    bool TransparentBackground,
    bool Animation,
    bool DisableDrawer,
    bool Showing);

public sealed record MkModalBrowserPlacement(
    string Type,
    bool Fixed,
    double? MaximumHeight,
    string TransformOrigin,
    double SourceWidth,
    int ZIndex);

public sealed record MkModalAttachment(
    IJSObjectReference Handle,
    MkModalBrowserPlacement Placement);

public interface IMkModalInterop : IAsyncDisposable
{
    ValueTask<MkModalAttachment> AttachAsync(
        ElementReference? source,
        ElementReference modal,
        ElementReference background,
        ElementReference content,
        DotNetObjectReference<MkModal> receiver,
        MkModalInteropOptions options,
        CancellationToken cancellationToken);
}

public sealed class MkModalInterop(IJSRuntime javascript) : IMkModalInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/modal.js"));

    public async ValueTask<MkModalAttachment> AttachAsync(
        ElementReference? source,
        ElementReference modal,
        ElementReference background,
        ElementReference content,
        DotNetObjectReference<MkModal> receiver,
        MkModalInteropOptions options,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        IJSObjectReference handle = await imported.InvokeAsync<IJSObjectReference>(
            "attachV12",
            cancellationToken,
            source,
            modal,
            background,
            content,
            receiver,
            options).ConfigureAwait(false);
        MkModalBrowserPlacement placement = await handle.InvokeAsync<MkModalBrowserPlacement>(
            "getPlacement",
            cancellationToken).ConfigureAwait(false);
        return new(handle, placement);
    }

    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated)
        {
            return;
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            IJSObjectReference imported = await module.Value.WaitAsync(cleanup.Token).ConfigureAwait(false);
            await imported.DisposeAsync().AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
