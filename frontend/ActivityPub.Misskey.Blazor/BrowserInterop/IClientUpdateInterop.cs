using ActivityPub.Misskey.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IClientUpdateInterop : IAsyncDisposable
{
    ValueTask<ClientVersionStorageSnapshot> SynchronizeVersionAsync(
        string currentVersion,
        CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> AttachDialogAsync(
        ElementReference modal,
        ElementReference content,
        ElementReference panel,
        DotNetObjectReference<MkUpdated> receiver,
        Uri releaseNotesUrl,
        CancellationToken cancellationToken);
}

public sealed record ClientVersionStorageSnapshot(
    string? PreviousVersion,
    bool Changed,
    bool Available = true,
    string? ErrorCode = null);

public sealed class ClientUpdateInterop(IJSRuntime javascript) : IClientUpdateInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/client-update.js"));

    public async ValueTask<ClientVersionStorageSnapshot> SynchronizeVersionAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        ValidateVersion(currentVersion);
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<ClientVersionStorageSnapshot>(
            "synchronizeVersion",
            cancellationToken,
            currentVersion).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> AttachDialogAsync(
        ElementReference modal,
        ElementReference content,
        ElementReference panel,
        DotNetObjectReference<MkUpdated> receiver,
        Uri releaseNotesUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(releaseNotesUrl);
        if (!releaseNotesUrl.IsAbsoluteUri || releaseNotesUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(releaseNotesUrl.Host, "misskey-hub.net", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Misskey release-notes URL is invalid.", nameof(releaseNotesUrl));
        }

        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "attachDialog",
            cancellationToken,
            modal,
            content,
            panel,
            receiver,
            releaseNotesUrl.AbsoluteUri).ConfigureAwait(false);
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

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl))
        {
            throw new ArgumentException("The client version is invalid.", nameof(version));
        }
    }
}
