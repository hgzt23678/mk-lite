using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IEmojiPickerInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference?> AttachAsync(
        ElementReference search,
        ElementReference emojis,
        DotNetObjectReference<MkEmojiPicker> receiver,
        CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference?>(null);

    ValueTask FocusAsync(ElementReference search, CancellationToken cancellationToken);

    ValueTask ResetAsync(ElementReference emojis, CancellationToken cancellationToken);
}

public sealed class EmojiPickerInterop(IJSRuntime jsRuntime) : IEmojiPickerInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference?> AttachAsync(
        ElementReference search,
        ElementReference emojis,
        DotNetObjectReference<MkEmojiPicker> receiver,
        CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/emoji-picker.js",
            cancellationToken);
        return await module.InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            search,
            emojis,
            receiver);
    }

    public async ValueTask FocusAsync(ElementReference search, CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/emoji-picker.js",
            cancellationToken);
        await module.InvokeVoidAsync("focus", cancellationToken, search);
    }

    public async ValueTask ResetAsync(ElementReference emojis, CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/emoji-picker.js",
            cancellationToken);
        await module.InvokeVoidAsync("reset", cancellationToken, emojis);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
    }
}
