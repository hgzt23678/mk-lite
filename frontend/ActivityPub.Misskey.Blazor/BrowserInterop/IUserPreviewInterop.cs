using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IUserPreviewInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachDirectiveHostAsync(
        DotNetObjectReference<UserPreviewDirectiveHost> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachPreviewAsync(
        string hostId,
        string sourceId,
        long generation,
        ElementReference preview,
        DotNetObjectReference<MkUserPreview> receiver,
        CancellationToken cancellationToken);
}

public sealed class UserPreviewInterop(IJSRuntime javascript) : IUserPreviewInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/user-preview.js"));

    public async ValueTask<IJSObjectReference> AttachDirectiveHostAsync(
        DotNetObjectReference<UserPreviewDirectiveHost> receiver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachDirectiveHost",
            cancellationToken,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachPreviewAsync(
        string hostId,
        string sourceId,
        long generation,
        ElementReference preview,
        DotNetObjectReference<MkUserPreview> receiver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachPreview",
            cancellationToken,
            hostId,
            sourceId,
            generation,
            preview,
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
