using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IWidgetsInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference?> AttachAsync(
        ElementReference root,
        DotNetObjectReference<MkWidgets> receiver,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IJSObjectReference?>(null);
}

public sealed class WidgetsInterop(IJSRuntime javascript) : IWidgetsInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference?> AttachAsync(
        ElementReference root,
        DotNetObjectReference<MkWidgets> receiver,
        CancellationToken cancellationToken = default)
    {
        module ??= await javascript.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./_content/ActivityPub.Misskey.Blazor/js/widgets.js");
        return await module.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            receiver);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        module = null;
    }
}
