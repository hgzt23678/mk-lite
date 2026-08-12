using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed class ThemeDeviceInterop(IJSRuntime javaScript) : IThemeDeviceInterop, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<bool> PrefersDarkAsync(CancellationToken cancellationToken = default)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<bool>("prefersDark", cancellationToken).ConfigureAwait(false);
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
            "/_content/ActivityPub.Misskey.Blazor/js/theme-device.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }
}
