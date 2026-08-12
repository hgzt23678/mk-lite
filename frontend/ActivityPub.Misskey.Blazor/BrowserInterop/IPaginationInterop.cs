using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record PaginationScrollSnapshot(
    double ScrollTop,
    double ScrollHeight,
    bool UsesWindow,
    bool AtBottom);

public interface IPaginationInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        DotNetObjectReference<T> receiver,
        bool enableAutoLoad,
        CancellationToken cancellationToken)
        where T : class;

    ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken);

    ValueTask<bool> IsBottomVisibleAsync(
        ElementReference root,
        double tolerance,
        CancellationToken cancellationToken);

    ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
        ElementReference root,
        CancellationToken cancellationToken);

    ValueTask RestoreScrollAsync(
        ElementReference root,
        PaginationScrollSnapshot snapshot,
        bool stickToBottom,
        CancellationToken cancellationToken);

    ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken);

    ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken);
}

public sealed class PaginationInterop(IJSRuntime javascript) : IPaginationInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/pagination.js"));

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        DotNetObjectReference<T> receiver,
        bool enableAutoLoad,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            receiver,
            enableAutoLoad).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsTopVisibleAsync(
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>("isTopVisible", cancellationToken, root).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsBottomVisibleAsync(
        ElementReference root,
        double tolerance,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>(
            "isBottomVisible",
            cancellationToken,
            root,
            tolerance).ConfigureAwait(false);
    }

    public async ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<PaginationScrollSnapshot>(
            "captureScroll",
            cancellationToken,
            root).ConfigureAwait(false);
    }

    public async ValueTask RestoreScrollAsync(
        ElementReference root,
        PaginationScrollSnapshot snapshot,
        bool stickToBottom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        await imported.InvokeVoidAsync(
            "restoreScroll",
            cancellationToken,
            root,
            snapshot,
            stickToBottom).ConfigureAwait(false);
    }

    public async ValueTask ScrollToTopAsync(
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        await imported.InvokeVoidAsync("scrollToTop", cancellationToken, root).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>("isWindowAtTop", cancellationToken).ConfigureAwait(false);
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
