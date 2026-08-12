using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class StickyContainerTests : BunitContext
{
    [Fact]
    public async Task PreservesPinnedDomStylesMeasurementAndRootFallthrough()
    {
        var interop = new RecordingStickyInterop();
        Services.AddSingleton<IStickyContainerInterop>(interop);

        IRenderedComponent<MkStickyContainer> component = Render<MkStickyContainer>(parameters => parameters
            .Add(sticky => sticky.Header, "header")
            .Add(sticky => sticky.ChildContent, "body")
            .Add(sticky => sticky.AdditionalAttributes, new Dictionary<string, object>
            {
                ["class"] = "contract-sticky",
                ["style"] = "color: rgb(1, 2, 3);",
                ["data-contract"] = "sticky"
            }));

        component.WaitForAssertion(() => Assert.Single(interop.Handles));
        IElement root = component.Find(".contract-sticky");
        IElement header = root.Children[0];
        IElement body = root.Children[1];
        Assert.Equal("sticky", root.GetAttribute("data-contract"));
        Assert.Contains("color: rgb(1, 2, 3)", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal("position: sticky; top: var(--stickyTop, 0); z-index: 1000;", header.GetAttribute("style"));
        Assert.Null(body.GetAttribute("data-sticky-container-header-height"));
        Assert.Equal("--stickyTop: 0px", body.GetAttribute("style"));

        await component.InvokeAsync(() => component.Instance.UpdateStickyHeaderHeight(48.5, 0));
        root = component.Find(".contract-sticky");
        body = root.Children[1];
        Assert.Equal("48.5", body.GetAttribute("data-sticky-container-header-height"));
        Assert.Equal("--stickyTop: 48.5px", body.GetAttribute("style"));

        MkStickyContainer instance = component.Instance;
        await instance.DisposeAsync();
        Assert.Equal(1, interop.Handles[instance].DisposeCalls);
        component.Dispose();
    }

    [Fact]
    public async Task CascadesTheMeasuredOffsetToNestedStickyContainers()
    {
        var interop = new RecordingStickyInterop();
        Services.AddSingleton<IStickyContainerInterop>(interop);

        IRenderedComponent<MkStickyContainer> host = Render<MkStickyContainer>(parameters => parameters
            .Add(sticky => sticky.Header, "outer header")
            .Add(sticky => sticky.ChildContent, builder =>
            {
                builder.OpenComponent<MkStickyContainer>(0);
                builder.AddAttribute(1, nameof(MkStickyContainer.Header), (RenderFragment)(nested => nested.AddContent(0, "inner header")));
                builder.AddAttribute(2, nameof(MkStickyContainer.ChildContent), (RenderFragment)(nested => nested.AddContent(0, "inner body")));
                builder.AddAttribute(3, "class", "inner-sticky");
                builder.CloseComponent();
            }));

        IRenderedComponent<MkStickyContainer> outer = host;
        IRenderedComponent<MkStickyContainer> inner = host.FindComponent<MkStickyContainer>();
        host.WaitForAssertion(() => Assert.Equal(2, interop.Handles.Count));

        await outer.InvokeAsync(() => outer.Instance.UpdateStickyHeaderHeight(64, 0));

        host.WaitForAssertion(() =>
        {
            Assert.Contains(64, interop.Handles[inner.Instance].ParentTopValues);
            IElement innerRoot = inner.Find(".inner-sticky");
            Assert.Equal("--stickyTop: 64px", innerRoot.Children[1].GetAttribute("style"));
            Assert.Contains("top: var(--stickyTop, 0)", innerRoot.Children[0].GetAttribute("style"), StringComparison.Ordinal);
        });
    }

    private sealed class RecordingStickyInterop : IStickyContainerInterop, IDisposable
    {
        public Dictionary<MkStickyContainer, RecordingHandle> Handles { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            MkStickyContainer component = Assert.IsType<MkStickyContainer>(receiver.Value);
            var handle = new RecordingHandle();
            handle.ParentTopValues.Add(parentTop);
            Handles.Add(component, handle);
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<double> ParentTopValues { get; } = [];

        public int DisposeCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (string.Equals(identifier, "setParentTop", StringComparison.Ordinal))
            {
                ParentTopValues.Add(Convert.ToDouble(Assert.Single(args ?? []), CultureInfo.InvariantCulture));
            }
            else if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
