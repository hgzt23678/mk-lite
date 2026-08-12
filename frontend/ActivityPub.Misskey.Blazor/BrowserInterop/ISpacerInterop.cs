using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record SpacerObservationOptions(string? OverriddenDeviceKind);

public interface ISpacerInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> ObserveAsync<T>(
        ElementReference element,
        SpacerObservationOptions options,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;
}

public sealed class SpacerInterop(IJSRuntime javascript) : ISpacerInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/spacer.js"));

    public async ValueTask<IJSObjectReference> ObserveAsync<T>(
        ElementReference element,
        SpacerObservationOptions options,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(options);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "observe",
            cancellationToken,
            element,
            options,
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
