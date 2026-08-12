using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IModalPageWindowInterop : IAsyncDisposable
{
    ValueTask<bool> OpenNewTabAsync(Uri url, CancellationToken cancellationToken);

    ValueTask<bool> PopoutAsync(
        Uri url,
        ElementReference window,
        CancellationToken cancellationToken);
}

public sealed class ModalPageWindowInterop(IJSRuntime javascript) : IModalPageWindowInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/modal-page-window.js"));

    public async ValueTask<bool> OpenNewTabAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>(
            "openNewTab",
            cancellationToken,
            url.AbsoluteUri).ConfigureAwait(false);
    }

    public async ValueTask<bool> PopoutAsync(
        Uri url,
        ElementReference window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>(
            "popout",
            cancellationToken,
            url.AbsoluteUri,
            window).ConfigureAwait(false);
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
