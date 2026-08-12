using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IButtonRippleInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        bool autofocus,
        CancellationToken cancellationToken) => AttachAsync(element, cancellationToken);
}

public sealed class ButtonRippleInterop(IJSRuntime javascript) : IButtonRippleInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/button-ripple.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        CancellationToken cancellationToken)
    {
        return await AttachAsync(element, autofocus: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        bool autofocus,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            element,
            autofocus).ConfigureAwait(false);
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
