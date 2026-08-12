using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IVisibilityTooltipInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachTriggerAsync(
        ElementReference target,
        DotNetObjectReference<MkVisibility> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachTooltipAsync(
        ElementReference target,
        ElementReference tooltip,
        DotNetObjectReference<MkTooltip> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachTooltipAsync(
        ElementReference target,
        ElementReference tooltip,
        DotNetObjectReference<MkTooltip> receiver,
        TooltipAttachmentOptions options,
        CancellationToken cancellationToken) =>
        AttachTooltipAsync(target, tooltip, receiver, cancellationToken);
}

public sealed record TooltipAttachmentOptions(
    double? X,
    double? Y,
    string Direction,
    int InnerMargin,
    bool Animation);

public sealed class VisibilityTooltipInterop(IJSRuntime javascript) : IVisibilityTooltipInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/visibility-tooltip.js"));

    public async ValueTask<IJSObjectReference> AttachTriggerAsync(
        ElementReference target,
        DotNetObjectReference<MkVisibility> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachTrigger",
            cancellationToken,
            target,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachTooltipAsync(
        ElementReference target,
        ElementReference tooltip,
        DotNetObjectReference<MkTooltip> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachTooltip",
            cancellationToken,
            target,
            tooltip,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachTooltipAsync(
        ElementReference target,
        ElementReference tooltip,
        DotNetObjectReference<MkTooltip> receiver,
        TooltipAttachmentOptions options,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachTooltip",
            cancellationToken,
            target,
            tooltip,
            receiver,
            options).ConfigureAwait(false);
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
