using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public sealed record ClipboardWriteResult(bool Succeeded, string Method, string? ErrorCode);

public interface IClipboardInterop : IAsyncDisposable
{
    ValueTask<ClipboardWriteResult> WriteTextAsync(string value, CancellationToken cancellationToken);
}

public sealed class ClipboardInterop(IJSRuntime javascript) : IClipboardInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/clipboard.js"));

    public async ValueTask<ClipboardWriteResult> WriteTextAsync(
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<ClipboardWriteResult>(
            "writeText",
            cancellationToken,
            value).ConfigureAwait(false);
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
