using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IAnalogClockInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        double thickness,
        double? offsetMinutes,
        bool twentyFourHour,
        string graduations,
        bool fadeGraduations,
        string secondHandAnimation,
        CancellationToken cancellationToken);
}

public sealed class AnalogClockInterop(IJSRuntime javascript) : IAnalogClockInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/analog-clock.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        double thickness,
        double? offsetMinutes,
        bool twentyFourHour,
        string graduations,
        bool fadeGraduations,
        string secondHandAnimation,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            element,
            thickness,
            offsetMinutes,
            twentyFourHour,
            graduations,
            fadeGraduations,
            secondHandAnimation).ConfigureAwait(false);
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
