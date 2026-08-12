using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface ITagCloudInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        string canvasId,
        string tagsId,
        ElementReference root,
        CancellationToken cancellationToken);
}

public sealed class TagCloudInterop(IJSRuntime javascript) : ITagCloudInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/tag-cloud.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        string canvasId,
        string tagsId,
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            canvasId,
            tagsId,
            root).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated)
        {
            return;
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            IJSObjectReference imported = await module.Value.WaitAsync(cleanup.Token).ConfigureAwait(false);
            await imported.DisposeAsync().AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
