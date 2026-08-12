using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface INavbarInterop : IAsyncDisposable
{
    ValueTask SubmitAsync(ElementReference form, CancellationToken cancellationToken);
}

public sealed class NavbarInterop(IJSRuntime javascript) : INavbarInterop
{
    private IJSObjectReference? module;

    public async ValueTask SubmitAsync(ElementReference form, CancellationToken cancellationToken)
    {
        module ??= await BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/navbar.js",
            cancellationToken).ConfigureAwait(false);
        await module.InvokeVoidAsync("submit", cancellationToken, form).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
