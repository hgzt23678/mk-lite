using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface ICaptchaInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        DotNetObjectReference<Components.MkCaptcha> receiver,
        string provider,
        string siteKey,
        string? action,
        string? cdata,
        bool darkMode,
        CancellationToken cancellationToken);
}

public sealed class CaptchaInterop(IJSRuntime jsRuntime) : ICaptchaInterop
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachAsync(
        ElementReference root,
        DotNetObjectReference<Components.MkCaptcha> receiver,
        string provider,
        string siteKey,
        string? action,
        string? cdata,
        bool darkMode,
        CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/captcha.js",
            cancellationToken);
        return await module.InvokeAsync<IJSObjectReference>(
            "attachCaptcha",
            cancellationToken,
            root,
            receiver,
            provider,
            siteKey,
            action,
            cdata,
            darkMode);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
