namespace ActivityPub.Misskey.Blazor.State;

public enum ClientStorageArea
{
    Local,
    Session
}

public interface IClientStorage
{
    ValueTask<T?> ReadAsync<T>(ClientStorageArea area, string key, CancellationToken cancellationToken = default);
    ValueTask WriteAsync<T>(ClientStorageArea area, string key, T value, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(ClientStorageArea area, string key, CancellationToken cancellationToken = default);
}
