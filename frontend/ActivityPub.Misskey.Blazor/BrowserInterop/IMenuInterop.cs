using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IMenuInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        ElementReference items,
        bool viaKeyboard,
        DotNetObjectReference<MkMenu> receiver,
        CancellationToken cancellationToken);

    ValueTask PositionChildAsync(
        ElementReference child,
        ElementReference target,
        ElementReference root,
        CancellationToken cancellationToken);
}

public sealed class MenuInterop(IJSRuntime javascript) : IMenuInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/menu.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        ElementReference items,
        bool viaKeyboard,
        DotNetObjectReference<MkMenu> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            items,
            viaKeyboard,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask PositionChildAsync(
        ElementReference child,
        ElementReference target,
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        await imported.InvokeVoidAsync(
            "positionChild",
            cancellationToken,
            child,
            target,
            root).ConfigureAwait(false);
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
