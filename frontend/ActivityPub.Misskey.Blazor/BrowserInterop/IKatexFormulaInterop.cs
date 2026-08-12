using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IKatexFormulaInterop : IAsyncDisposable
{
    ValueTask EnsureLoadedAsync(CancellationToken cancellationToken);

    ValueTask RenderAsync(
        ElementReference element,
        string formula,
        CancellationToken cancellationToken);
}

public sealed class KatexFormulaInterop(IJSRuntime javascript) : IKatexFormulaInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/katex-renderer.js"));

    public async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken) =>
        _ = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask RenderAsync(
        ElementReference element,
        string formula,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formula);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        await imported.InvokeVoidAsync(
            "renderFormula",
            cancellationToken,
            element,
            formula).ConfigureAwait(false);
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
