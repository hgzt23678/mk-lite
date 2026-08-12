using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IMarqueeInterop : IAsyncDisposable
{
    ValueTask<double> SetDurationAsync(
        ElementReference content,
        int repeat,
        double duration,
        CancellationToken cancellationToken);
}

public sealed class MarqueeInterop(IJSRuntime javascript) : IMarqueeInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/marquee.js"));

    public async ValueTask<double> SetDurationAsync(
        ElementReference content,
        int repeat,
        double duration,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<double>(
            "setDuration",
            cancellationToken,
            content,
            repeat,
            duration).ConfigureAwait(false);
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
