namespace ActivityPub.Misskey.Blazor.Components;

internal sealed class MisskeyFormRadiosContext<TValue>(
    Func<TValue> readValue,
    Func<TValue, Task> selectAsync)
{
    public TValue Value => readValue();

    public Task SelectAsync(TValue value) => selectAsync(value);
}
