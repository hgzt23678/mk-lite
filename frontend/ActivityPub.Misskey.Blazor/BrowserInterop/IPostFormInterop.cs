using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IPostFormInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> ObserveSizeAsync(
        ElementReference root,
        DotNetObjectReference<MkPostForm> receiver,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachDropTargetAsync(
        ElementReference root,
        ElementReference input,
        CancellationToken cancellationToken);

    ValueTask OpenFilesAsync(ElementReference input, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<string>> CreatePreviewUrlsAsync(
        ElementReference input,
        CancellationToken cancellationToken);

    ValueTask InsertTextAsync(
        ElementReference textarea,
        string value,
        CancellationToken cancellationToken);

    ValueTask FocusAsync(ElementReference textarea, CancellationToken cancellationToken);

    ValueTask RevokePreviewUrlsAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken);

    ValueTask<AutocompleteContext> GetAutocompleteContextAsync(
        ElementReference textarea,
        CancellationToken cancellationToken);

    ValueTask CompleteAutocompleteAsync(
        ElementReference textarea,
        int start,
        int endOffset,
        string replacement,
        CancellationToken cancellationToken);
}

public sealed record AutocompleteContext(string Line, double X, double Y, int CaretStart);

public sealed class PostFormInterop(IJSRuntime jsRuntime) : IPostFormInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> ObserveSizeAsync(
        ElementReference root,
        DotNetObjectReference<MkPostForm> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<IJSObjectReference>(
            "observeSize",
            cancellationToken,
            root,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask OpenFilesAsync(ElementReference input, CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("openFiles", cancellationToken, input).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachDropTargetAsync(
        ElementReference root,
        ElementReference input,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<IJSObjectReference>(
            "attachDropTarget",
            cancellationToken,
            root,
            input).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> CreatePreviewUrlsAsync(
        ElementReference input,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        string[] urls = await loaded.InvokeAsync<string[]>("createPreviewUrls", cancellationToken, input).ConfigureAwait(false);
        return urls;
    }

    public async ValueTask InsertTextAsync(
        ElementReference textarea,
        string value,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("insertText", cancellationToken, textarea, value).ConfigureAwait(false);
    }

    public async ValueTask FocusAsync(ElementReference textarea, CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("focus", cancellationToken, textarea).ConfigureAwait(false);
    }

    public async ValueTask RevokePreviewUrlsAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return;
        }

        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("revokePreviewUrls", cancellationToken, urls).ConfigureAwait(false);
    }

    public async ValueTask<AutocompleteContext> GetAutocompleteContextAsync(
        ElementReference textarea,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<AutocompleteContext>(
            "getAutocompleteContext",
            cancellationToken,
            textarea).ConfigureAwait(false);
    }

    public async ValueTask CompleteAutocompleteAsync(
        ElementReference textarea,
        int start,
        int endOffset,
        string replacement,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync(
            "completeAutocomplete",
            cancellationToken,
            textarea,
            start,
            endOffset,
            replacement).ConfigureAwait(false);
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/post-form.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
    }
}
