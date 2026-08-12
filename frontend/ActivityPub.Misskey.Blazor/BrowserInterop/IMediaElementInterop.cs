using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IMediaElementInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachVolumeAsync(
        ElementReference element,
        double initialVolume,
        DotNetObjectReference<MkMediaBanner> receiver,
        CancellationToken cancellationToken);
}

public sealed class MediaElementInterop(IJSRuntime javascript) : IMediaElementInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/media-element.js"));

    public async ValueTask<IJSObjectReference> AttachVolumeAsync(
        ElementReference element,
        double initialVolume,
        DotNetObjectReference<MkMediaBanner> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachVolume",
            cancellationToken,
            element,
            initialVolume,
            receiver).ConfigureAwait(false);
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
