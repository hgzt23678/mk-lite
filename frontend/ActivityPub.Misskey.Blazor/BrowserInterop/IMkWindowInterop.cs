using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record MkWindowInteropOptions(
    double? InitialWidth,
    double? InitialHeight,
    bool CanResize,
    bool Front,
    bool Animation);

public sealed record MkWindowAttachment(
    IJSObjectReference Handle,
    MkWindowBrowserState State);

public interface IMkWindowInterop : IAsyncDisposable
{
    ValueTask<MkWindowAttachment> AttachAsync(
        ElementReference root,
        ElementReference body,
        ElementReference title,
        DotNetObjectReference<MkWindow> receiver,
        MkWindowInteropOptions options,
        CancellationToken cancellationToken);
}

public sealed class MkWindowInterop(IJSRuntime javascript) : IMkWindowInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/window.js"));

    public async ValueTask<MkWindowAttachment> AttachAsync(
        ElementReference root,
        ElementReference body,
        ElementReference title,
        DotNetObjectReference<MkWindow> receiver,
        MkWindowInteropOptions options,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        IJSObjectReference handle = await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            body,
            title,
            receiver,
            options).ConfigureAwait(false);
        MkWindowBrowserState state = await handle.InvokeAsync<MkWindowBrowserState>(
            "getState",
            cancellationToken).ConfigureAwait(false);
        return new(handle, state);
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
