using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record DateSeparatedCalendarPart(int Month, int Day);

public interface IDateSeparatedListInterop : IAsyncDisposable
{
    ValueTask<DateSeparatedCalendarPart[]> GetCalendarPartsAsync(
        IReadOnlyList<long> unixTimeMilliseconds,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        CancellationToken cancellationToken);
}

public sealed class DateSeparatedListInterop(IJSRuntime javascript) : IDateSeparatedListInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/date-separated-list.js"));

    public async ValueTask<DateSeparatedCalendarPart[]> GetCalendarPartsAsync(
        IReadOnlyList<long> unixTimeMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unixTimeMilliseconds);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<DateSeparatedCalendarPart[]>(
            "getCalendarParts",
            cancellationToken,
            unixTimeMilliseconds).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
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
