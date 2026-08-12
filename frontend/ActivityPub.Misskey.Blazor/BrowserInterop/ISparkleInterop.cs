using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface ISparkleInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference content,
        DotNetObjectReference<MkSparkle> receiver,
        long generation,
        bool animationEnabled,
        CancellationToken cancellationToken);
}

public sealed class SparkleInterop(IJSRuntime javascript) : ISparkleInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/sparkle.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference content,
        DotNetObjectReference<MkSparkle> receiver,
        long generation,
        bool animationEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            content,
            receiver,
            generation,
            animationEnabled).ConfigureAwait(false);
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
