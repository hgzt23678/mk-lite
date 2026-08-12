using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IPrismSyntaxHighlightInterop : IAsyncDisposable
{
    ValueTask EnsureLoadedAsync(CancellationToken cancellationToken);

    ValueTask<string> HighlightAsync(
        ElementReference element,
        string code,
        string? language,
        CancellationToken cancellationToken);
}

public sealed class PrismSyntaxHighlightInterop(IJSRuntime javascript) : IPrismSyntaxHighlightInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/prism-highlight.js"));

    public async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken) =>
        _ = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<string> HighlightAsync(
        ElementReference element,
        string code,
        string? language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<string>(
            "highlight",
            cancellationToken,
            element,
            code,
            language).ConfigureAwait(false);
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
