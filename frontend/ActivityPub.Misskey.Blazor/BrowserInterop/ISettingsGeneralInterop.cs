using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

/// <summary>
/// Browser-only state used by Misskey v12's general settings page.
/// These values intentionally remain raw localStorage strings because that is
/// the storage contract used by the upstream client (not JSON/Pizzax state).
/// </summary>
public interface ISettingsGeneralInterop
{
    ValueTask<string?> ReadRawAsync(string key, CancellationToken cancellationToken = default);

    ValueTask WriteRawAsync(string key, string value, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    ValueTask ApplySystemFontAsync(bool enabled, CancellationToken cancellationToken = default);
}

public sealed class SettingsGeneralInterop(IJSRuntime javaScript) : ISettingsGeneralInterop, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<string?> ReadRawAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<string?>("readRaw", cancellationToken, key).ConfigureAwait(false);
    }

    public async ValueTask WriteRawAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("writeRaw", cancellationToken, key, value).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("remove", cancellationToken, key).ConfigureAwait(false);
    }

    public async ValueTask ApplySystemFontAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("applySystemFont", cancellationToken, enabled).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        await module.DisposeAsync().ConfigureAwait(false);
        module = null;
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        return module ??= await BrowserModuleImporter.ImportAsync(
            javaScript,
            "/_content/ActivityPub.Misskey.Blazor/js/settings-general.js",
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateKey(string key)
    {
        if (key is not ("fontSize" or "useSystemFont"))
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
}
