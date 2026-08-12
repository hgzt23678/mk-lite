using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IBlurhashImageInterop : IAsyncDisposable
{
    ValueTask<bool> DrawAsync(
        ElementReference canvas,
        ElementReference image,
        string? hash,
        int size,
        CancellationToken cancellationToken);
}

public sealed class BlurhashImageInterop(IJSRuntime javascript) : IBlurhashImageInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/blurhash-image.js"));

    public async ValueTask<bool> DrawAsync(
        ElementReference canvas,
        ElementReference image,
        string? hash,
        int size,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>(
            "draw",
            cancellationToken,
            canvas,
            image,
            hash,
            size).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated)
        {
            return;
        }

        try
        {
            IJSObjectReference imported = await module.Value.ConfigureAwait(false);
            await imported.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
