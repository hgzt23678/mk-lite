using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

internal static class BrowserModuleImporter
{
    internal const string PageDisposalMarker = "MISSKEY_INTEROP_PAGE_DISPOSAL";

    public static async Task<IJSObjectReference> ImportAsync(
        IJSRuntime javascript,
        string specifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await javascript.InvokeAsync<IJSObjectReference>(
                "activityPubMisskeyInterop.importModule",
                cancellationToken,
                specifier).ConfigureAwait(false);
        }
        catch (JSException exception) when (IsPageDisposalImportFailure(exception))
        {
            // WebKit reports an aborted dynamic import as a plain JSException while the old
            // document is being discarded. Translate only the marker emitted by our lifecycle
            // bootstrap; genuine syntax, network, and CSP failures retain their original type.
            throw new JSDisconnectedException(
                "The browser document was disposed while loading a Misskey interop module.");
        }
    }

    internal static bool IsPageDisposalImportFailure(JSException exception) =>
        exception.Message.StartsWith(PageDisposalMarker, StringComparison.Ordinal);
}
