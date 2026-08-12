using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IAutocompleteInterop
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference textarea,
        ElementReference root,
        DotNetObjectReference<MkAutocomplete> receiver,
        CancellationToken cancellationToken);

    ValueTask FocusSuggestionAsync(ElementReference list, int index, CancellationToken cancellationToken);

    ValueTask DisposeAsync(IJSObjectReference attachment, CancellationToken cancellationToken);
}

public sealed class AutocompleteInterop(IJSRuntime jsRuntime) : IAutocompleteInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference textarea,
        ElementReference root,
        DotNetObjectReference<MkAutocomplete> receiver,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<IJSObjectReference>(
            "attachAutocomplete",
            cancellationToken,
            textarea,
            root,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask FocusSuggestionAsync(
        ElementReference list,
        int index,
        CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync(
            "focusAutocompleteItem",
            cancellationToken,
            list,
            index).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync(IJSObjectReference attachment, CancellationToken cancellationToken)
    {
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await loaded.InvokeVoidAsync("disposeAutocomplete", cancellationToken, attachment).ConfigureAwait(false);
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/autocomplete.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }
}
