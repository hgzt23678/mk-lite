using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ButtonTests : BunitContext
{
    [Fact]
    public void ButtonBranchPreservesPropsClassOrderDomClickAndAutofocusContract()
    {
        var interop = new RecordingButtonRippleInterop();
        Services.AddSingleton<IButtonRippleInterop>(interop);
        MouseEventArgs? click = null;

        IRenderedComponent<MkButton> component = Render<MkButton>(parameters => parameters
            .Add(button => button.Type, "submit")
            .Add(button => button.Primary, true)
            .Add(button => button.Gradate, true)
            .Add(button => button.Rounded, true)
            .Add(button => button.Inline, true)
            .Add(button => button.Autofocus, true)
            .Add(button => button.Wait, true)
            .Add(button => button.Danger, true)
            .Add(button => button.Full, true)
            .Add(button => button.CssClassAdditional, "fixture")
            .Add(button => button.OnClick, value => click = value)
            .AddUnmatched("class", "fallthrough")
            .AddUnmatched("data-fixture", "button")
            .AddChildContent("実行"));

        IElement button = component.Find("button");
        Assert.Equal(
            "bghgjjyj _button inline primary gradate danger rounded full fixture fallthrough",
            button.GetAttribute("class"));
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Equal("button", button.GetAttribute("data-fixture"));
        Assert.Null(button.GetAttribute("disabled"));
        Assert.Null(button.GetAttribute("wait"));
        Assert.NotNull(button.QuerySelector(":scope > .ripples:empty"));
        Assert.Equal("実行", button.QuerySelector(":scope > .content")?.TextContent);
        component.WaitForAssertion(() => Assert.True(interop.Autofocus));

        button.Click(new MouseEventArgs { ClientX = 12, ClientY = 8 });

        MouseEventArgs recorded = Assert.IsType<MouseEventArgs>(click);
        Assert.Equal(12, recorded.ClientX);
        Assert.Equal(8, recorded.ClientY);
    }

    [Fact]
    public void LinkBranchPreservesAnchorDomWithoutEmittingTheButtonClickEvent()
    {
        var interop = new RecordingButtonRippleInterop();
        Services.AddSingleton<IButtonRippleInterop>(interop);
        int clicks = 0;

        IRenderedComponent<MkButton> component = Render<MkButton>(parameters => parameters
            .Add(button => button.Link, true)
            .Add(button => button.To, "/about")
            .Add(button => button.Primary, true)
            .Add(button => button.OnClick, () => clicks++)
            .AddChildContent("詳細"));

        Assert.Empty(component.FindAll("button"));
        IElement link = component.Find("a.bghgjjyj._button.primary");
        Assert.Equal("/about", link.GetAttribute("href"));
        Assert.NotNull(link.QuerySelector(":scope > .ripples:empty"));
        Assert.Equal("詳細", link.QuerySelector(":scope > .content")?.TextContent);

        Assert.Throws<MissingEventHandlerException>(() => link.Click());

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void WaitRemainsAnUnusedUpstreamPropAndDoesNotDisableOrReplaceContent()
    {
        Services.AddSingleton<IButtonRippleInterop>(new RecordingButtonRippleInterop());

        IRenderedComponent<MkButton> component = Render<MkButton>(parameters => parameters
            .Add(button => button.Wait, true)
            .AddChildContent("変更しない"));

        IElement button = component.Find("button.bghgjjyj._button");
        Assert.Null(button.GetAttribute("disabled"));
        Assert.Equal("変更しない", button.QuerySelector(":scope > .content")?.TextContent);
        Assert.DoesNotContain("wait", button.ClassList);
        Assert.Empty(button.QuerySelectorAll(".spinner, [aria-busy=true]"));
    }

    [Fact]
    public async Task DisposalCancelsTheAttachmentAndDisposesItsJavascriptHandle()
    {
        var interop = new RecordingButtonRippleInterop();
        Services.AddSingleton<IButtonRippleInterop>(interop);
        IRenderedComponent<MkButton> component = Render<MkButton>();
        component.WaitForAssertion(() => Assert.True(interop.Attached));

        await component.Instance.DisposeAsync();

        Assert.Contains("dispose", interop.Reference.Invocations);
        Assert.True(interop.Reference.Disposed);
    }

    private sealed class RecordingButtonRippleInterop : IButtonRippleInterop
    {
        public RecordingJsObjectReference Reference { get; } = new();

        public bool Attached { get; private set; }

        public bool Autofocus { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => AttachAsync(element, autofocus: false, cancellationToken);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken)
        {
            Attached = true;
            Autofocus = autofocus;
            return ValueTask.FromResult<IJSObjectReference>(Reference);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsObjectReference : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
