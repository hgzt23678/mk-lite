using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IAboutMisskeyPhysicsInterop : IAsyncDisposable
{
    ValueTask<bool> PrepareAsync(ElementReference container, CancellationToken cancellationToken);

    ValueTask<IJSObjectReference> StartAsync(ElementReference container, CancellationToken cancellationToken);
}

public sealed class AboutMisskeyPhysicsInterop(IJSRuntime javascript) : IAboutMisskeyPhysicsInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/about-misskey-physics.js"));

    public async ValueTask<bool> PrepareAsync(
        ElementReference container,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<bool>("prepare", cancellationToken, container).ConfigureAwait(false);
    }

    public async ValueTask<IJSObjectReference> StartAsync(
        ElementReference container,
        CancellationToken cancellationToken)
    {
        IJSObjectReference imported = await module.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await imported.InvokeAsync<IJSObjectReference>(
            "start",
            cancellationToken,
            container).ConfigureAwait(false);
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
