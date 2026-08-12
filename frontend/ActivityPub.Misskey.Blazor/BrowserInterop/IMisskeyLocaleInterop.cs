using ActivityPub.Misskey.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IMisskeyLocaleInterop
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        IReadOnlyList<MisskeyLocaleDefinition> supportedLocales,
        string currentLocale,
        string direction,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken = default)
        where T : class;
}

public sealed class MisskeyLocaleInterop(IJSRuntime javaScript) : IMisskeyLocaleInterop, IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        IReadOnlyList<MisskeyLocaleDefinition> supportedLocales,
        string currentLocale,
        string direction,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(supportedLocales);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentLocale);
        ArgumentNullException.ThrowIfNull(receiver);
        IJSObjectReference loaded = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await loaded.InvokeAsync<IJSObjectReference>(
            "attachLocale",
            cancellationToken,
            supportedLocales,
            currentLocale,
            direction,
            receiver).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync().ConfigureAwait(false);
            module = null;
        }
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            javaScript,
            "/_content/ActivityPub.Misskey.Blazor/js/localization.js",
            cancellationToken).ConfigureAwait(false);
        return module;
    }
}
