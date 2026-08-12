using ActivityPub.Misskey.Blazor.BrowserInterop;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.State;

public interface IThemeInterop
{
    ValueTask<bool> ApplyAsync(
        ThemeDefinition theme,
        CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class ThemeInterop(IJSRuntime javaScript) : IThemeInterop, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<bool> ApplyAsync(
        ThemeDefinition theme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<bool>(
            "applyTheme",
            cancellationToken,
            theme.Properties,
            theme.Base,
            true,
            theme.Id).ConfigureAwait(false);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("clearTheme", cancellationToken).ConfigureAwait(false);
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
        module ??= await BrowserModuleImporter.ImportAsync(
            javaScript,
            "/_content/ActivityPub.Misskey.Blazor/js/theme.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }
}
