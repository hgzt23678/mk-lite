using System.Text.Json;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

/// <summary>
/// The compatibility boundary for Misskey v12's idb-proxy.  It is separate
/// from ordinary local/session settings so account-switch records do not get
/// silently moved into a different storage area.
/// </summary>
public interface IMisskeyIndexedStorage : IAsyncDisposable
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class MisskeyIndexedStorage(IJSRuntime javascript) : IMisskeyIndexedStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IJSObjectReference? module;

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        string? json = await loaded.InvokeAsync<string?>("get", cancellationToken, key).ConfigureAwait(false);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await (await ModuleAsync(cancellationToken).ConfigureAwait(false)).InvokeVoidAsync("set", cancellationToken, key, json).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await (await ModuleAsync(cancellationToken).ConfigureAwait(false)).InvokeVoidAsync("delete", cancellationToken, key).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            module = null;
        }
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken) =>
        module ??= await BrowserModuleImporter.ImportAsync(
            javascript,
            "/_content/ActivityPub.Misskey.Blazor/js/misskey-indexed-storage.js",
            cancellationToken).ConfigureAwait(false);

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl))
        {
            throw new ArgumentException("The indexed storage key is invalid.", nameof(key));
        }
    }
}
