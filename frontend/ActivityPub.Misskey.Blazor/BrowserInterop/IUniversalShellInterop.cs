using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IUniversalShellInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;
}

public sealed class UniversalShellInterop(IJSRuntime javascript) : IUniversalShellInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/universal-shell.js"));

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated)
        {
            return;
        }

        try
        {
            IJSObjectReference imported = await module.Value.ConfigureAwait(false);
            await imported.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}
