using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IElementSizeInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> ObserveAsync<T>(
        ElementReference element,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;
}

public sealed class ElementSizeInterop(IJSRuntime javascript) : IElementSizeInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/element-size.js"));

    public async ValueTask<IJSObjectReference> ObserveAsync<T>(
        ElementReference element,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "observe",
            cancellationToken,
            element,
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
    }
}
