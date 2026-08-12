using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface ITimeInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        DotNetObjectReference<MkTime> receiver,
        long generation,
        long unixTimeMilliseconds,
        bool updateRelativeTime,
        CancellationToken cancellationToken);
}

public sealed class TimeInterop(IJSRuntime javascript) : ITimeInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/time.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        DotNetObjectReference<MkTime> receiver,
        long generation,
        long unixTimeMilliseconds,
        bool updateRelativeTime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            element,
            receiver,
            generation,
            unixTimeMilliseconds,
            updateRelativeTime).ConfigureAwait(false);
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
