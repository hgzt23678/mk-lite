using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IDigitalClockInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        bool showSeconds,
        bool showMilliseconds,
        int? offsetMinutes,
        CancellationToken cancellationToken);
}

public sealed class DigitalClockInterop(IJSRuntime javascript) : IDigitalClockInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/digital-clock.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        bool showSeconds,
        bool showMilliseconds,
        int? offsetMinutes,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            element,
            showSeconds,
            showMilliseconds,
            offsetMinutes).ConfigureAwait(false);
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
