using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IFolderInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        ElementReference content,
        string? persistKey,
        bool expanded,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;
}

public sealed record FolderBrowserState(bool Expanded, string Background, bool Narrow);

public sealed class FolderInterop(IJSRuntime javascript) : IFolderInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/folder.js"));

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        ElementReference content,
        string? persistKey,
        bool expanded,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            content,
            persistKey,
            expanded,
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
