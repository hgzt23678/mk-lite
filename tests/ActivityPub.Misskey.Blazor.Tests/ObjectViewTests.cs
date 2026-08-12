using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ObjectViewTests : BunitContext
{
    public ObjectViewTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new ObjectViewLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new ObjectViewInstanceService());
    }

    [Fact]
    public void PreservesPinnedTypeClassesValuesAndInitialNestedCollapse()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{"nil":null,"enabled":true,"title":"<safe>","count":1234,"empty":[],"nested":{"answer":42},"items":[false,"x"]}""");

        IRenderedComponent<MkObjectView> component = Render<MkObjectView>(parameters => parameters
            .Add(view => view.Value, document.RootElement.Clone()));

        IElement root = component.Find(".zhyxdalp > .igpposuu > .object");
        Assert.Equal("null", FindValue(root, "nil").TextContent);
        Assert.Equal("true", FindValue(root, "enabled").TextContent);
        Assert.Contains("\"<safe>\"", FindValue(root, "title").TextContent, StringComparison.Ordinal);
        Assert.Equal("1,234", FindValue(root, "count").TextContent);
        Assert.Equal("[]", FindValue(root, "empty").TextContent);
        Assert.Equal("{...}", FindValue(root, "nested").TextContent);
        Assert.Equal("[...]", FindValue(root, "items").TextContent);
        Assert.Contains("boolean true", component.Find(".boolean.true").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void ReactionIconDelegatesThePinnedCustomEmojiAndNoStyleContract()
    {
        IRenderedComponent<MkReactionIcon> custom = Render<MkReactionIcon>(parameters => parameters
            .Add(icon => icon.Reaction, ":party_parrot:")
            .Add(icon => icon.NoStyle, true)
            .Add(icon => icon.CustomEmojis,
            [
                new EmojiPickerCustomEmoji("party_parrot", "/media/parrot.webp", null, [])
            ]));

        IElement image = custom.Find("img.mk-emoji.custom.normal.noStyle");
        Assert.Equal(":party_parrot:", image.GetAttribute("alt"));
        Assert.Equal("/media/parrot.webp", image.GetAttribute("src"));

        IRenderedComponent<MkReactionIcon> unicode = Render<MkReactionIcon>(parameters => parameters
            .Add(icon => icon.Reaction, "👍"));
        Assert.Contains("/twemoji/1f44d.svg", unicode.Find("img.mk-emoji").GetAttribute("src"), StringComparison.Ordinal);
        Assert.DoesNotContain("normal", unicode.Find("img.mk-emoji").ClassList);
    }

    [Fact]
    public void ReactionTooltipPreservesPinnedIconNameAndWidthContract()
    {
        var browser = new RecordingVisibilityTooltipInterop();
        Services.AddSingleton<IVisibilityTooltipInterop>(browser);

        IRenderedComponent<MkReactionTooltip> component = Render<MkReactionTooltip>(parameters => parameters
            .Add(tooltip => tooltip.Reaction, ":party@.:")
            .Add(tooltip => tooltip.CustomEmojis, Array.Empty<EmojiPickerCustomEmoji>())
            .Add(tooltip => tooltip.Target, default));

        IElement root = component.Find("div.buebdbiu[role=tooltip]");
        Assert.Contains("max-width: 340px", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal(":party:", root.QuerySelector(".beeadbfb > .name")?.TextContent);
        Assert.Contains("icon", root.QuerySelector(".beeadbfb > .icon")?.ClassName, StringComparison.Ordinal);
        component.WaitForAssertion(() => Assert.True(browser.TooltipAttached));
    }

    [Fact]
    public void TooltipPreservesCoordinateDirectionTextAndInnerMarginContract()
    {
        var browser = new RecordingVisibilityTooltipInterop();
        Services.AddSingleton<IVisibilityTooltipInterop>(browser);

        IRenderedComponent<MkTooltip> component = Render<MkTooltip>(parameters => parameters
            .Add(tooltip => tooltip.X, 120)
            .Add(tooltip => tooltip.Y, 80)
            .Add(tooltip => tooltip.Direction, "right")
            .Add(tooltip => tooltip.InnerMargin, 16)
            .Add(tooltip => tooltip.Text, "coordinate tooltip")
            .Add(tooltip => tooltip.Showing, true));

        Assert.Equal("coordinate tooltip", component.Find(".buebdbiu > span").TextContent);
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(browser.Options);
            Assert.Equal(120d, browser.Options.X);
            Assert.Equal(80d, browser.Options.Y);
            Assert.Equal("right", browser.Options.Direction);
            Assert.Equal(16, browser.Options.InnerMargin);
        });
    }

    [Fact]
    public void InstanceTickerPreservesPinnedDomGradientAndAttributeFallthrough()
    {
        IRenderedComponent<MkInstanceTicker> component = Render<MkInstanceTicker>(parameters => parameters
            .Add(ticker => ticker.Instance, new("remote.example", "/media/remote.ico", "#123456"))
            .AddUnmatched("class", "ticker")
            .AddUnmatched("data-probe", "instance"));

        IElement root = component.Find(".hpaizdrt.ticker[data-probe=instance]");
        Assert.Contains("linear-gradient(90deg, #123456, #12345600)", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal("/media/remote.ico", root.QuerySelector(":scope > img.icon")?.GetAttribute("src"));
        Assert.Equal("remote.example", root.QuerySelector(":scope > span.name")?.TextContent);
    }

    [Fact]
    public void InstanceTickerUsesTheLocalInstanceFallbackAndRejectsUnsafeRemotePresentationValues()
    {
        IRenderedComponent<MkInstanceTicker> local = Render<MkInstanceTicker>();
        Assert.Equal("Local Misskey", local.Find(".hpaizdrt > .name").TextContent);
        Assert.Contains("#86b30000", local.Find(".hpaizdrt").GetAttribute("style"), StringComparison.Ordinal);

        IRenderedComponent<MkInstanceTicker> unsafeRemote = Render<MkInstanceTicker>(parameters => parameters
            .Add(ticker => ticker.Instance, new("unsafe.example", "https://remote.example/icon.png", "red;display:none")));
        Assert.Empty(unsafeRemote.FindAll(".hpaizdrt > img.icon"));
        Assert.Contains("#77777700", unsafeRemote.Find(".hpaizdrt").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void FeaturedPhotosPreservesPinnedRootAndVueAttributeFallthrough()
    {
        IRenderedComponent<MkFeaturedPhotos> component = Render<MkFeaturedPhotos>(parameters => parameters
            .Add(value => value.CssClass, "bg")
            .AddUnmatched("class", "cover")
            .AddUnmatched("style", "height: 40px")
            .AddUnmatched("data-probe", "featured"));

        IElement root = component.Find(".xfbouadm.bg.cover[data-probe=featured]");
        Assert.Contains("background-image: url('/media/background.jpg')", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("height: 40px", root.GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void RouterPlaceholdersPreserveThePinnedLoadingAndEmptyBranches()
    {
        IRenderedComponent<LoadingPlaceholder> loading = Render<LoadingPlaceholder>();
        Assert.NotNull(loading.Find("._root_13vug_9[role=status]"));

        IRenderedComponent<EmptyPlaceholder> empty = Render<EmptyPlaceholder>();
        Assert.Equal("<div></div>", empty.Markup);
    }

    [Fact]
    public void GoogleSearchPreservesPinnedDomAndAttachesTheNativeClickBoundary()
    {
        var browser = new RecordingGoogleSearchInterop();
        Services.AddSingleton<IGoogleSearchInterop>(browser);

        using IRenderedComponent<MkGoogle> component = Render<MkGoogle>(parameters => parameters
            .Add(search => search.Q, "misskey federation"));

        IElement root = component.Find("div.mk-google");
        IElement input = root.QuerySelector(":scope > input[type=search]")!;
        Assert.Equal("misskey federation", input.GetAttribute("value"));
        Assert.Equal("misskey federation", input.GetAttribute("placeholder"));
        Assert.Equal("Googleで検索", root.QuerySelector(":scope > button")?.TextContent.Trim());
        component.WaitForAssertion(() => Assert.True(browser.Attached));
    }

    [Fact]
    public void ExpandsAndCollapsesObjectsAndArraysWithPinnedOneBasedArrayLabels()
    {
        var value = new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, object?> { ["answer"] = 42 },
            ["items"] = new object?[] { false, "x" }
        };
        IRenderedComponent<MkObjectView> component = Render<MkObjectView>(parameters => parameters
            .Add(view => view.Value, value));

        IElement nested = FindEntry(component.Find(".zhyxdalp > .igpposuu > .object"), "nested");
        nested.QuerySelector("button.toggle")!.Click();
        nested = FindEntry(component.Find(".zhyxdalp > .igpposuu > .object"), "nested");
        Assert.Contains("answer:", nested.TextContent, StringComparison.Ordinal);
        Assert.Contains("42", nested.TextContent, StringComparison.Ordinal);

        IElement items = FindEntry(component.Find(".zhyxdalp > .igpposuu > .object"), "items");
        items.QuerySelector("button._button")!.Click();
        items = FindEntry(component.Find(".zhyxdalp > .igpposuu > .object"), "items");
        Assert.Equal(["1: false", "2: \"x\""], items.QuerySelectorAll(".array > .element")
            .Select(element => element.TextContent.Trim()).ToArray());
        items.QuerySelector("button.toggle")!.Click();
        Assert.Equal("[...]", FindValue(component.Find(".zhyxdalp > .igpposuu > .object"), "items").TextContent);
    }

    private static IElement FindEntry(IElement root, string key) => root.QuerySelectorAll(":scope > .kv")
        .Single(element => element.QuerySelector(":scope > .k")?.TextContent == key + ":");

    private static IElement FindValue(IElement root, string key) =>
        FindEntry(root, key).QuerySelector(":scope > .v")!;

    private sealed class ObjectViewLocalizer : IMisskeyLocalizer
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            key == "searchByGoogle" ? "Googleで検索" : key;

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingGoogleSearchInterop : IGoogleSearchInterop
    {
        public bool Attached { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference button,
            CancellationToken cancellationToken)
        {
            Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsObjectReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ObjectViewInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "Local Misskey",
            "fixture",
            "12.119.2",
            "/static-assets/favicon.png",
            "/media/background.jpg",
            null,
            false,
            false,
            false,
            null));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class RecordingVisibilityTooltipInterop : IVisibilityTooltipInterop
    {
        public bool TooltipAttached { get; private set; }
        public TooltipAttachmentOptions? Options { get; private set; }

        public ValueTask<IJSObjectReference> AttachTriggerAsync(
            ElementReference target,
            DotNetObjectReference<MkVisibility> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsObjectReference());

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            CancellationToken cancellationToken)
        {
            TooltipAttached = true;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsObjectReference());
        }

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            TooltipAttachmentOptions options,
            CancellationToken cancellationToken)
        {
            Options = options;
            return AttachTooltipAsync(target, tooltip, receiver, cancellationToken);
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
}
