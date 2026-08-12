using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.State;

public sealed class BrowserStorage(IJSRuntime javaScript) : IClientStorage, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SensitiveKeySegments = ["token", "authorization", "cookie", "secret"];
    private IJSObjectReference? module;

    public async ValueTask<T?> ReadAsync<T>(ClientStorageArea area, string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        string? json = await loaded.InvokeAsync<string?>("read", cancellationToken, AreaName(area), key).ConfigureAwait(false);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async ValueTask WriteAsync<T>(ClientStorageArea area, string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await loaded.InvokeVoidAsync("write", cancellationToken, AreaName(area), key, json).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(ClientStorageArea area, string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("remove", cancellationToken, AreaName(area), key).ConfigureAwait(false);
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
            "/_content/ActivityPub.Misskey.Blazor/js/storage.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }

    private static string AreaName(ClientStorageArea area) => area switch
    {
        ClientStorageArea.Local => "local",
        ClientStorageArea.Session => "session",
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl) ||
            SensitiveKeySegments.Any(segment => key.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The client storage key is invalid or reserved for security-sensitive state.", nameof(key));
        }
    }
}
