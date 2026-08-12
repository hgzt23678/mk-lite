using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IDialogWindowInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;

    ValueTask<IJSObjectReference> AttachHighPriorityAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class => AttachAsync(modal, content, window, receiver, cancellationToken);

    ValueTask<IJSObjectReference> AttachMiddlePriorityAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class => AttachAsync(modal, content, window, receiver, cancellationToken);
}

public sealed class DialogWindowInterop(IJSRuntime javascript) : IDialogWindowInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/dialog-window.js"));

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
        => await AttachAsync(modal, content, window, receiver, "low", cancellationToken).ConfigureAwait(false);

    public async ValueTask<IJSObjectReference> AttachHighPriorityAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
        => await AttachAsync(modal, content, window, receiver, "high", cancellationToken).ConfigureAwait(false);

    public async ValueTask<IJSObjectReference> AttachMiddlePriorityAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
        => await AttachAsync(modal, content, window, receiver, "middle", cancellationToken).ConfigureAwait(false);

    private async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference modal,
        ElementReference content,
        ElementReference window,
        DotNetObjectReference<T> receiver,
        string priority,
        CancellationToken cancellationToken)
        where T : class
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            modal,
            content,
            window,
            receiver,
            priority).ConfigureAwait(false);
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
