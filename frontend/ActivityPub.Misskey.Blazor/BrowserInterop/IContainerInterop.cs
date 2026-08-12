using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IContainerInterop : IAsyncDisposable
{
    ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        ElementReference header,
        ElementReference content,
        double? maxHeight,
        bool expanded,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class;
}

internal sealed class ContainerInterop(IJSRuntime javascript) : IContainerInterop
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        BrowserModuleImporter.ImportAsync(
            javascript,
            "./_content/ActivityPub.Misskey.Blazor/js/container.js"));

    public async ValueTask<IJSObjectReference> AttachAsync<T>(
        ElementReference root,
        ElementReference header,
        ElementReference content,
        double? maxHeight,
        bool expanded,
        DotNetObjectReference<T> receiver,
        CancellationToken cancellationToken)
        where T : class => await (await module.Value.ConfigureAwait(false)).InvokeAsync<IJSObjectReference>(
            "attach",
            cancellationToken,
            root,
            header,
            content,
            maxHeight,
            expanded,
            receiver);

    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated)
        {
            IJSObjectReference reference = await module.Value.ConfigureAwait(false);
            await reference.DisposeAsync().ConfigureAwait(false);
        }
    }
}
