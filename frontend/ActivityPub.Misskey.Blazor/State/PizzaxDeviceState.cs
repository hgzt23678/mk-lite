using System.Text.Json;

namespace ActivityPub.Misskey.Blazor.State;

public interface IPizzaxDeviceState
{
    ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default);

    ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default);
}

public sealed class PizzaxDeviceState(IClientStorage storage) : IPizzaxDeviceState
{
    private const string StorageKey = "pizzax::base";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<T> ReadAsync<T>(
        string propertyName,
        T fallback,
        CancellationToken cancellationToken = default)
    {
        ValidateProperty(propertyName);
        Dictionary<string, JsonElement>? state = await storage.ReadAsync<Dictionary<string, JsonElement>>(
            ClientStorageArea.Local,
            StorageKey,
            cancellationToken);
        if (state is null || !state.TryGetValue(propertyName, out JsonElement value))
        {
            return fallback;
        }

        return value.Deserialize<T>(JsonOptions) ?? fallback;
    }

    public async ValueTask WriteAsync<T>(
        string propertyName,
        T value,
        CancellationToken cancellationToken = default)
    {
        ValidateProperty(propertyName);
        Dictionary<string, JsonElement> state = await storage.ReadAsync<Dictionary<string, JsonElement>>(
            ClientStorageArea.Local,
            StorageKey,
            cancellationToken) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        state[propertyName] = JsonSerializer.SerializeToElement(value, JsonOptions);
        await storage.WriteAsync(ClientStorageArea.Local, StorageKey, state, cancellationToken);
    }

    private static void ValidateProperty(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || propertyName.Length > 128 || propertyName.Any(char.IsControl))
        {
            throw new ArgumentException("The Pizzax device property is invalid.", nameof(propertyName));
        }
    }
}
