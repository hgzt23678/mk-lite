using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface INotePageInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference container,
        DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
        long generation,
        string phase,
        CancellationToken cancellationToken);
}

public sealed class NotePageInterop(IJSRuntime javascript) : INotePageInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/note-page.js"));

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference container,
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
            container,
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
