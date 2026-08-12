using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MediaCaptionTests : BunitContext
{
    private readonly RecordingDialogInterop dialog = new();
    private readonly RecordingCaptionInterop caption = new();

    public MediaCaptionTests()
    {
        Services.AddSingleton<IDialogWindowInterop>(dialog);
        Services.AddSingleton<IMediaCaptionInterop>(caption);
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonRippleInterop());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
    }

    [Fact]
    public void PreservesPinnedDialogInputPreviewFooterAndAccessibilityHierarchy()
    {
        using IRenderedComponent<MkMediaCaption> component = Render<MkMediaCaption>(parameters => parameters
            .Add(item => item.Image, Image())
            .Add(item => item.Title, "画像の説明")
            .Add(item => item.Input, new MisskeyMediaCaptionInput("新しい説明を入力", "既存の説明"))
            .AddUnmatched("class", "contract-caption")
            .AddUnmatched("data-contract", "media-caption"));

        IElement root = component.Find(".qzhlnise.dialog.contract-caption[data-contract=media-caption]");
        Assert.Equal("dialog", root.GetAttribute("role"));
        Assert.Equal("true", root.GetAttribute("aria-modal"));
        Assert.NotNull(root.QuerySelector(":scope > .bg._modalBg"));
        Assert.NotNull(root.QuerySelector(":scope > .content > .container"));

        IElement editor = component.Find(".container > .top-caption > .mk-dialog");
        Assert.Equal("画像の説明", editor.QuerySelector(":scope > header > .title")?.TextContent);
        Assert.Equal("507", editor.QuerySelector(":scope > header > .text-count")?.TextContent);
        IElement textarea = Assert.IsAssignableFrom<IElement>(editor.QuerySelector(":scope > textarea"));
        Assert.Equal("既存の説明", textarea.GetAttribute("value"));
        Assert.Equal("新しい説明を入力", textarea.GetAttribute("placeholder"));
        Assert.Equal("true", textarea.GetAttribute("data-mk-autofocus"));
        Assert.True(textarea.HasAttribute("autofocus"));

        IElement preview = component.Find(".container > .hdrwpsaf");
        Assert.Equal("fixture.png", preview.QuerySelector(":scope > header")?.TextContent);
        IElement image = Assert.IsAssignableFrom<IElement>(preview.QuerySelector(":scope > img"));
        Assert.Equal("/static-assets/favicon.png", image.GetAttribute("src"));
        Assert.Equal("fixture description", image.GetAttribute("alt"));
        Assert.Equal("fixture description", image.GetAttribute("title"));
        Assert.Equal("button", image.GetAttribute("role"));
        Assert.Equal("0", image.GetAttribute("tabindex"));
        Assert.Equal(
            ["image/png", "3KB", "1,920px × 1,080px"],
            preview.QuerySelectorAll(":scope > footer > span").Select(item => item.TextContent.Trim()));
        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, dialog.AttachCalls);
            Assert.Equal(1, caption.AttachCalls);
        });
    }

    [Fact]
    public void UsesStringzEquivalentTextElementsAndDisablesOnlyTheVisibleOkButtonWhenOverLimit()
    {
        using IRenderedComponent<MkMediaCaption> component = RenderCaption();
        IElement textarea = component.Find("textarea");

        textarea.Input("👨‍👩‍👧‍👦");
        Assert.Equal("511", component.Find(".text-count").TextContent);

        textarea.Input(new string('a', 513));
        Assert.Equal("-1", component.Find(".text-count.over").TextContent);
        Assert.True(component.Find(".buttons > .primary").HasAttribute("disabled"));
        Assert.False(component.Find(".buttons > button:not(.primary)").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ConfirmAndCtrlEnterEmitOneResultThenClose()
    {
        MisskeyMediaCaptionResult? result = null;
        int closed = 0;
        using IRenderedComponent<MkMediaCaption> component = Render<MkMediaCaption>(parameters => parameters
            .Add(item => item.Image, Image())
            .Add(item => item.Title, "画像の説明")
            .Add(item => item.Input, new MisskeyMediaCaptionInput("新しい説明を入力", "before"))
            .Add(item => item.Done, value => result = value)
            .Add(item => item.Closed, () => closed++));

        component.Find("textarea").Input("after");
        await component.InvokeAsync(component.Instance.NotifyCtrlEnter);

        Assert.Equal(new MisskeyMediaCaptionResult(Canceled: false, Result: "after"), result);
        Assert.Equal(1, dialog.Handle.CloseCalls);
        await component.InvokeAsync(component.Instance.NotifyClosed);
        Assert.Equal(1, closed);

        await component.InvokeAsync(component.Instance.NotifyCtrlEnter);
        Assert.Equal(1, dialog.Handle.CloseCalls);
    }

    [Fact]
    public async Task BackgroundCancelsEvenWhenDeclaredGuardIsFalseWhilePreviewClosesWithoutDone()
    {
        MisskeyMediaCaptionResult? backgroundResult = null;
        using IRenderedComponent<MkMediaCaption> background = Render<MkMediaCaption>(parameters => parameters
            .Add(item => item.Image, Image())
            .Add(item => item.Input, new MisskeyMediaCaptionInput())
            .Add(item => item.CancelableByBgClick, false)
            .Add(item => item.Done, value => backgroundResult = value));

        background.Find(".bg").Click();
        Assert.Equal(new MisskeyMediaCaptionResult(Canceled: true, Result: null), backgroundResult);

        MisskeyMediaCaptionResult? previewResult = null;
        int previewClosed = 0;
        using IRenderedComponent<MkMediaCaption> preview = Render<MkMediaCaption>(parameters => parameters
            .Add(item => item.Image, Image())
            .Add(item => item.Input, new MisskeyMediaCaptionInput())
            .Add(item => item.Done, value => previewResult = value)
            .Add(item => item.Closed, () => previewClosed++));

        preview.Find(".hdrwpsaf > img").Click();
        await preview.InvokeAsync(preview.Instance.NotifyClosed);

        Assert.Null(previewResult);
        Assert.Equal(1, previewClosed);
    }

    [Fact]
    public void DoesNotRenderAnUnproxiedRemoteImage()
    {
        using IRenderedComponent<MkMediaCaption> component = Render<MkMediaCaption>(parameters => parameters
            .Add(item => item.Image, Image() with { Url = "https://tracker.invalid/pixel.png" })
            .Add(item => item.Input, new MisskeyMediaCaptionInput()));

        Assert.Empty(component.FindAll(".qzhlnise"));
        Assert.DoesNotContain("tracker.invalid", component.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<MkMediaCaption> RenderCaption() => Render<MkMediaCaption>(parameters => parameters
        .Add(item => item.Image, Image())
        .Add(item => item.Title, "画像の説明")
        .Add(item => item.Input, new MisskeyMediaCaptionInput("新しい説明を入力", "before")));

    private static ComposerMediaViewModel Image() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        "fixture.png",
        "image/png",
        "/static-assets/favicon.png",
        "/static-assets/favicon.png",
        Sensitive: false,
        Description: "fixture description",
        Width: 1920,
        Height: 1080,
        Size: 2560);

    private sealed class RecordingDialogInterop : IDialogWindowInterop
    {
        public RecordingJsObject Handle { get; } = new();
        public int AttachCalls { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingCaptionInterop : IMediaCaptionInterop
    {
        public int AttachCalls { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference textarea,
            DotNetObjectReference<MkMediaCaption> receiver,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsObject());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsObject());

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MfmNode>>(
                [new("text", JsonSerializer.SerializeToElement(new { text }), null)]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "ok" => "OK",
            "cancel" => "Cancel",
            "close" => "Close",
            "describeFile" => "Add caption",
            "inputNewDescription" => "Enter caption",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingJsObject : IJSObjectReference
    {
        public int CloseCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (string.Equals(identifier, "close", StringComparison.Ordinal))
            {
                CloseCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return InvokeAsync<TValue>(identifier, args);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
