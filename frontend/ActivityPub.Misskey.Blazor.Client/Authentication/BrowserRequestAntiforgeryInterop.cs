using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class BrowserRequestAntiforgeryInterop(IJSRuntime javascript) : IAsyncDisposable
{
    private IJSObjectReference? module;

    public async ValueTask ReplaceAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        IJSObjectReference requestSecurity = module ??= await javascript.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./_content/ActivityPub.Misskey.Blazor/js/frontend-request-security.js").ConfigureAwait(false);
        await requestSecurity.InvokeVoidAsync(
            "requireAntiforgeryHeader",
            cancellationToken).ConfigureAwait(false);
        await requestSecurity.InvokeVoidAsync(
            "replaceAntiforgeryToken",
            cancellationToken,
            token).ConfigureAwait(false);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        if (module is not null)
        {
            await module.InvokeVoidAsync("clearAntiforgeryToken", cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }
        try
        {
            await module.InvokeVoidAsync("clearAntiforgeryToken").ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
