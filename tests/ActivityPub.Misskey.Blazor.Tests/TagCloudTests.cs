using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TagCloudTests : BunitContext
{
    private readonly RecordingTagCloudInterop interop = new();

    [Fact]
    public void RendersPinnedSurfaceCanvasAndHiddenTagsSlot()
    {
        Services.AddSingleton<ITagCloudInterop>(interop);
        using IRenderedComponent<MkTagCloud> component = Render<MkTagCloud>(parameters => parameters
            .Add(cloud => cloud.ChildContent, builder =>
            {
                builder.OpenElement(0, "li");
                builder.AddContent(1, "#misskey");
                builder.CloseElement();
            }));

        component.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));
        Assert.NotNull(component.Find(".meijqfqm > canvas.canvas[width=300][height=300]"));
        Assert.Contains("<li>#misskey</li>", component.Find(".meijqfqm > .tags > ul").InnerHtml, StringComparison.Ordinal);
        Assert.Equal(16, interop.CanvasId.Length);
        Assert.Equal(16, interop.TagsId.Length);
    }

    private sealed class RecordingTagCloudInterop : ITagCloudInterop
    {
        public int AttachCalls { get; private set; }

        public string CanvasId { get; private set; } = string.Empty;

        public string TagsId { get; private set; } = string.Empty;

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            string canvasId,
            string tagsId,
            ElementReference root,
            CancellationToken cancellationToken)
        {
            _ = root;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            CanvasId = canvasId;
            TagsId = tagsId;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _ = identifier;
            _ = args;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
