using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface ICustomCssInterop
{
    ValueTask<string?> ReadStoredAsync(CancellationToken cancellationToken = default);

    ValueTask WriteStoredAsync(string css, CancellationToken cancellationToken = default);

    ValueTask ApplyAsync(string css, CancellationToken cancellationToken = default);
}

public sealed class CustomCssInterop(IJSRuntime javaScript) : ICustomCssInterop, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<string?> ReadStoredAsync(CancellationToken cancellationToken = default)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<string?>("readStored", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteStoredAsync(string css, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(css);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("writeStored", cancellationToken, css).ConfigureAwait(false);
    }

    public async ValueTask ApplyAsync(string css, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(css);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("apply", cancellationToken, css).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync().ConfigureAwait(false);
            module = null;
        }
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        return module ??= await BrowserModuleImporter.ImportAsync(
            javaScript,
            "/_content/ActivityPub.Misskey.Blazor/js/custom-css.js",
            cancellationToken).ConfigureAwait(false);
    }
}
