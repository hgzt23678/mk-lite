using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed class FormSuspenseTransitionReceiver(
    Func<long, string, Task> completed)
{
    [JSInvokable]
    public Task NotifyTransitionCompleted(long generation, string phase) =>
        completed(generation, phase);
}

public interface IFormSuspenseInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
        long generation,
        string phase,
        CancellationToken cancellationToken);
}

public sealed class FormSuspenseInterop(IJSRuntime javascript) : IFormSuspenseInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/form-suspense.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference element,
        DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
        long generation,
        string phase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
        if (phase is not ("enter" or "leave"))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            element,
            receiver,
            generation,
            phase).ConfigureAwait(false);
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
