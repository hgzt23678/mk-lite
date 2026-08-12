using System.Reflection;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Routing;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class KeyValueTests : BunitContext
{
    [Fact]
    public void ComponentPreservesPinnedSlotsClassesCopyControlAndAttributeFallthrough()
    {
        var clipboard = new RecordingClipboardInterop(new ClipboardWriteResult(true, "async-clipboard", null));
        var feedback = new MisskeyTransientFeedbackService();
        Services.AddSingleton<IClipboardInterop>(clipboard);
        Services.AddSingleton<IMisskeyTransientFeedbackService>(feedback);

        IRenderedComponent<MkKeyValue> component = Render<MkKeyValue>(parameters => parameters
            .Add(item => item.Copy, "12.119.2")
            .Add(item => item.Key, builder => builder.AddContent(0, "バージョン"))
            .Add(item => item.Value, builder => builder.AddContent(0, "12.119.2"))
            .AddUnmatched("class", "_formBlock fixture-key-value")
            .AddUnmatched("data-contract", "copy"));

        IElement root = component.Find("div.alqyeyti._formBlock.fixture-key-value");
        Assert.Equal("copy", root.GetAttribute("data-contract"));
        Assert.Equal("バージョン", root.QuerySelector(":scope > .key")?.TextContent);
        Assert.StartsWith("12.119.2", root.QuerySelector(":scope > .value")?.TextContent, StringComparison.Ordinal);

        IElement button = component.Find(".alqyeyti > .value > button._textButton");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("margin-left: 0.5em;", button.GetAttribute("style"));
        Assert.Equal("コピー", button.GetAttribute("title"));
        Assert.Equal("コピー", button.GetAttribute("aria-label"));
        Assert.NotNull(button.QuerySelector(":scope > i.far.fa-copy[aria-hidden=true]"));
    }

    [Fact]
    public void OnelineAndFalsyCopyMatchThePinnedConditionalBranches()
    {
        Services.AddSingleton<IClipboardInterop>(
            new RecordingClipboardInterop(new ClipboardWriteResult(true, "exec-command", null)));
        Services.AddSingleton<IMisskeyTransientFeedbackService>(new MisskeyTransientFeedbackService());

        IRenderedComponent<MkKeyValue> component = Render<MkKeyValue>(parameters => parameters
            .Add(item => item.Oneline, true)
            .Add(item => item.Copy, string.Empty)
            .Add(item => item.Key, builder => builder.AddContent(0, "ID"))
            .Add(item => item.Value, builder => builder.AddContent(0, "9duke7z2w3")));

        Assert.NotNull(component.Find(".alqyeyti.oneline > .key"));
        Assert.NotNull(component.Find(".alqyeyti.oneline > .value"));
        Assert.Empty(component.FindAll("button"));
    }

    [Fact]
    public void SuccessfulClipboardWriteIsTheOnlyPathThatShowsSuccessFeedback()
    {
        var clipboard = new RecordingClipboardInterop(new ClipboardWriteResult(true, "async-clipboard", null));
        var feedback = new MisskeyTransientFeedbackService();
        Services.AddSingleton<IClipboardInterop>(clipboard);
        Services.AddSingleton<IMisskeyTransientFeedbackService>(feedback);
        IRenderedComponent<MkKeyValue> component = Render<MkKeyValue>(parameters => parameters
            .Add(item => item.Copy, "secret-is-never-rendered-as-an-attribute")
            .Add(item => item.CopySuccessAnnouncement, "コピーしました"));

        component.Find("button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("secret-is-never-rendered-as-an-attribute", clipboard.WrittenValue);
            Assert.Equal("コピーしました", Assert.Single(feedback.Entries).Announcement);
        });
        Assert.DoesNotContain("secret-is-never-rendered-as-an-attribute", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLIPBOARD_WRITE_FAILED")]
    [InlineData("REMOTE_EXCEPTION_TEXT")]
    public void FailedClipboardWriteDoesNotClaimSuccessAndOnlyExposesASafeErrorCode(string returnedCode)
    {
        var clipboard = new RecordingClipboardInterop(new ClipboardWriteResult(false, "none", returnedCode));
        var feedback = new MisskeyTransientFeedbackService();
        Services.AddSingleton<IClipboardInterop>(clipboard);
        Services.AddSingleton<IMisskeyTransientFeedbackService>(feedback);
        string? observedFailure = null;
        IRenderedComponent<MkKeyValue> component = Render<MkKeyValue>(parameters => parameters
            .Add(item => item.Copy, "copy-value")
            .Add(item => item.CopyFailed, code => observedFailure = code));

        component.Find("button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(feedback.Entries);
            Assert.Equal("CLIPBOARD_WRITE_FAILED", observedFailure);
            Assert.Equal("コピーできませんでした", component.Find("[role=alert]").TextContent);
        });
        Assert.DoesNotContain("REMOTE_EXCEPTION_TEXT", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessFeedbackPreservesThePinnedWaitingDialogSurfaceAndAddsALiveAnnouncement()
    {
        var feedback = new MisskeyTransientFeedbackService();
        var interop = new RecordingSuccessFeedbackInterop();
        Services.AddSingleton<IMisskeyTransientFeedbackService>(feedback);
        Services.AddSingleton<ISuccessFeedbackInterop>(interop);
        Guid id = feedback.ShowSuccess("コピーしました");

        IRenderedComponent<MkSuccessFeedback> component = Render<MkSuccessFeedback>(parameters => parameters
            .Add(item => item.Id, id)
            .Add(item => item.Announcement, "コピーしました"));

        IElement root = component.Find(".qzhlnise.dialog.modal-enter-active.modal-enter-from");
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Equal("polite", root.GetAttribute("aria-live"));
        Assert.Equal("true", root.GetAttribute("aria-atomic"));
        Assert.Equal("コピーしました", root.GetAttribute("aria-label"));
        Assert.NotNull(root.QuerySelector(":scope > .bg._modalBg"));
        Assert.NotNull(root.QuerySelector(":scope > .content > .iuyakobc.iconOnly > i.fas.fa-check.icon.success"));
        Assert.Equal("コピーしました", root.QuerySelector(".mk-visually-hidden")?.TextContent);
        component.WaitForAssertion(() => Assert.True(interop.Attached));
    }

    [Fact]
    public void ProductionFrontendHasNoTestRouteOrTestHostReferenceAndAdditionalRoutesAreExplicitlyTyped()
    {
        Assembly frontend = typeof(App).Assembly;
        string[] routeTemplates = frontend.GetTypes()
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(inherit: false))
            .Select(route => route.Template)
            .ToArray();

        Assert.DoesNotContain(routeTemplates, route => route.StartsWith("/__test", StringComparison.Ordinal));
        Assert.DoesNotContain(
            frontend.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                "ActivityPub.Misskey.Blazor.TestHost",
                StringComparison.Ordinal));
        Assert.Empty(MisskeyFrontendRouteAssemblies.Empty.Assemblies);
        Assert.Throws<ArgumentException>(() =>
            MisskeyFrontendRouteAssemblies.FromRouteComponents(typeof(NonRouteComponent)));

        MisskeyFrontendRouteAssemblies explicitRoutes =
            MisskeyFrontendRouteAssemblies.FromRouteComponents(typeof(ContractRouteComponent));
        Assert.Equal(typeof(ContractRouteComponent).Assembly, Assert.Single(explicitRoutes.Assemblies));
    }

    private sealed class RecordingClipboardInterop(ClipboardWriteResult result) : IClipboardInterop
    {
        public string? WrittenValue { get; private set; }

        public ValueTask<ClipboardWriteResult> WriteTextAsync(
            string value,
            CancellationToken cancellationToken)
        {
            WrittenValue = value;
            return ValueTask.FromResult(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSuccessFeedbackInterop : ISuccessFeedbackInterop
    {
        public bool Attached { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference background,
            ElementReference content,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class
        {
            Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsObjectReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsObjectReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Route("/contract-route")]
    private sealed class ContractRouteComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, "contract");
        }
    }

    private sealed class NonRouteComponent : ComponentBase;
}
