using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MfmViewTests : BunitContext
{
    private readonly FixtureParser parser = new();

    public MfmViewTests()
    {
        Services.AddSingleton<IMfmParserInterop>(parser);
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            SourceUrl: null,
            new Uri("https://local.example", UriKind.Absolute)));
        Services.AddSingleton<IAuthenticatedActorContext>(new AnonymousActorContext());
        Services.AddSingleton<IPrismSyntaxHighlightInterop>(new FixturePrism());
        Services.AddSingleton<IKatexFormulaInterop>(new FixtureKatex());
        Services.AddSingleton<IGoogleSearchInterop>(new FixtureGoogle());
        Services.AddSingleton<ISparkleInterop>(new FixtureSparkle());
        Services.AddSingleton<IMisskeyLocalizer>(new FixtureLocalizer());
    }

    [Fact]
    public void MfmPreservesPinnedNodesAuthorFallbackRoutesEmojiAndDisabledMotion()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animatedMfm: false));
        parser.Nodes =
        [
            Node("text", new { text = "first\nsecond" }),
            Node("bold", new { }, Text("bold")),
            Node("url", new { url = "https://xn--bcher-kva.example:8443/a%20b?q=x%20y#z%20z" }),
            Node("link", new { url = "https://local.example/tags/local" }, Text("local link")),
            Node("mention", new { username = "bob", host = (string?)null }),
            Node("hashtag", new { hashtag = "topic" }),
            Node("emojiCode", new { name = "party" }),
            Node("unicodeEmoji", new { emoji = "👍" }),
            Node("fn", new { name = "jelly", args = new { speed = "2s" } }, Text("motion")),
            Node("fn", new { name = "unknown", args = new { } }, Text("literal")),
            Node("fn", new { name = "sparkle", args = new { } }, Text("sparkle child")),
            Node("quote", new { }, Text("quoted"))
        ];
        var author = new NoteAuthorViewModel(
            "author-id",
            "author",
            "author@remote.example",
            "Author",
            "/avatar.png",
            IsBot: false);

        using IRenderedComponent<MfmView> component = Render<MfmView>(parameters => parameters
            .Add(value => value.Text, "fixture")
            .Add(value => value.Author, author)
            .Add(value => value.IsNote, false)
            .Add(value => value.CustomEmojis, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["party"] = "/media/party.webp"
            })
            .Add(value => value.CssClass, "slot-class")
            .AddUnmatched("class", "fallthrough")
            .AddUnmatched("data-contract", "mfm"));

        component.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement root = component.Find("span.havbbuyv.slot-class.fallthrough");
            Assert.Equal("mfm", root.GetAttribute("data-contract"));
            Assert.Single(root.QuerySelectorAll(":scope > br"));
            Assert.Equal("bold", root.QuerySelector(":scope > b")?.TextContent);

            AngleSharp.Dom.IElement url = component.Find("a.ieqqeuvs._link");
            Assert.Equal("https:", url.QuerySelector(":scope > .schema")?.TextContent);
            Assert.Equal("bücher.example", url.QuerySelector(":scope > .hostname")?.TextContent);
            Assert.Equal(":8443", url.QuerySelector(":scope > .port")?.TextContent);
            Assert.Equal("/a b", url.QuerySelector(":scope > .pathname")?.TextContent);
            Assert.Equal("?q=x y", url.QuerySelector(":scope > .query")?.TextContent);
            Assert.Equal("#z z", url.QuerySelector(":scope > .hash")?.TextContent);

            Assert.Equal("/tags/local", component.Find("a.xlcxczvw").GetAttribute("href"));
            Assert.Equal("/@bob@remote.example", component.Find("a.akbvjaqn").GetAttribute("href"));
            Assert.Equal("/explore/tags/topic", component.Find("a[href='/explore/tags/topic']").GetAttribute("href"));
            Assert.Equal("/media/party.webp", component.Find("img[alt=':party:']").GetAttribute("src"));
            Assert.EndsWith("/twemoji/1f44d.svg", component.Find("img[alt='👍']").GetAttribute("src"), StringComparison.Ordinal);

            AngleSharp.Dom.IElement motion = component.FindAll("span")
                .Single(element => element.TextContent == "motion");
            Assert.Equal("display: inline-block;", motion.GetAttribute("style"));
            Assert.DoesNotContain("animation", motion.GetAttribute("style"), StringComparison.Ordinal);
            Assert.Contains("$[unknown literal]", root.TextContent, StringComparison.Ordinal);
            Assert.Contains("sparkle child", root.TextContent, StringComparison.Ordinal);
            Assert.Empty(component.FindComponents<MkSparkle>());
            Assert.Equal("quoted", component.Find("div.quote").TextContent);
        });
    }

    [Fact]
    public void MfmUsesPinnedCodeFormulaSearchAndEnabledSparkleComponents()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animatedMfm: true));
        parser.Nodes =
        [
            Node("blockCode", new { code = "const x = 1;", lang = "javascript" }),
            Node("inlineCode", new { code = "inline" }),
            Node("mathBlock", new { formula = "x^2" }),
            Node("mathInline", new { formula = "y^2" }),
            Node("search", new { query = "Misskey" }),
            Node("fn", new { name = "sparkle", args = new { } }, Text("shiny")),
            Node("fn", new { name = "spin", args = new { left = true, speed = "3s" } }, Text("spin"))
        ];

        using IRenderedComponent<MfmView> component = Render<MfmView>(parameters => parameters
            .Add(value => value.Text, "rich fixture"));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, component.FindComponents<MkCode>().Count);
            Assert.Equal(2, component.FindComponents<MkFormula>().Count);
            Assert.Single(component.FindComponents<MkGoogle>());
            Assert.Single(component.FindComponents<MkSparkle>());
            Assert.Equal("const x = 1;", component.Find("pre > code").TextContent);
            Assert.Equal("inline", component.Find("code:not(pre > code)").TextContent);
            Assert.Equal("Misskey", component.Find(".mk-google input").GetAttribute("value"));
            Assert.Contains(
                "animation: mfm-spin 3s linear infinite; animation-direction: reverse;",
                component.Markup,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PlainNowrapMfmUsesNoteTagRouteAndDoesNotEmitRootForEmptyText()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animatedMfm: true));
        parser.Nodes =
        [
            Node("text", new { text = "one\ntwo" }),
            Node("hashtag", new { hashtag = "note" })
        ];

        using IRenderedComponent<MfmView> component = Render<MfmView>(parameters => parameters
            .Add(value => value.Text, "plain")
            .Add(value => value.Plain, true)
            .Add(value => value.NoWrap, true));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("one two#note", component.Find("span.havbbuyv.nowrap").TextContent);
            Assert.Empty(component.FindAll("br"));
            Assert.Equal("/tags/note", component.Find("a").GetAttribute("href"));
        });

        component.Render(parameters => parameters.Add(value => value.Text, string.Empty));
        Assert.DoesNotContain("havbbuyv", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UrlPreservesPinnedExternalSegmentsAndLocalSelfLink()
    {
        using IRenderedComponent<MkUrl> external = Render<MkUrl>(parameters => parameters
            .Add(value => value.Url, "https://xn--bcher-kva.example:8443/a%20b?q=x%20y#z%20z")
            .Add(value => value.Relationship, "nofollow noopener")
            .AddUnmatched("data-url", "external"));

        AngleSharp.Dom.IElement link = external.Find("a.ieqqeuvs._link");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("nofollow noopener", link.GetAttribute("rel"));
        Assert.Equal("external", link.GetAttribute("data-url"));
        Assert.Equal("https:", link.QuerySelector(".schema")?.TextContent);
        Assert.Equal("bücher.example", link.QuerySelector(".hostname")?.TextContent);
        Assert.Equal(":8443", link.QuerySelector(".port")?.TextContent);
        Assert.Equal("/a b", link.QuerySelector(".pathname")?.TextContent);

        using IRenderedComponent<MkUrl> local = Render<MkUrl>(parameters => parameters
            .Add(value => value.Url, "https://local.example/"));
        AngleSharp.Dom.IElement localLink = local.Find("a.ieqqeuvs._link");
        Assert.Equal("/", localLink.GetAttribute("href"));
        Assert.Null(localLink.GetAttribute("target"));
        Assert.Equal("local.example", localLink.QuerySelector(".self")?.TextContent);

        using IRenderedComponent<MkLink> contentLink = Render<MkLink>(parameters => parameters
            .Add(value => value.Url, "https://local.example/notes/one")
            .Add(value => value.Relationship, "nofollow noopener")
            .Add(value => value.ChildContent, builder => builder.AddContent(0, "note"))
            .AddUnmatched("class", "fallthrough")
            .AddUnmatched("data-link", "content"));
        AngleSharp.Dom.IElement contentAnchor = contentLink.Find("a.xlcxczvw._link.fallthrough");
        Assert.Equal("/notes/one", contentAnchor.GetAttribute("href"));
        Assert.Equal("content", contentAnchor.GetAttribute("data-link"));
        Assert.Equal("note", contentAnchor.TextContent);
        Assert.Empty(contentAnchor.QuerySelectorAll(":scope > .icon"));
    }

    private static MfmNode Text(string value) => Node("text", new { text = value });

    private static MfmNode Node(string type, object props, params MfmNode[] children) =>
        new(type, JsonSerializer.SerializeToElement(props), children.Length == 0 ? null : children);

    private sealed class FixtureParser : IMfmParserInterop
    {
        public IReadOnlyList<MfmNode> Nodes { get; set; } = [];

        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Nodes);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState(bool animatedMfm) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName == "animatedMfm" || propertyName == "animation"
                ? animatedMfm
                : fallback!;
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class AnonymousActorContext : IAuthenticatedActorContext
    {
        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedActor?>(null);

        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixturePrism : IPrismSyntaxHighlightInterop
    {
        public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<string> HighlightAsync(
            ElementReference element,
            string code,
            string? language,
            CancellationToken cancellationToken) => ValueTask.FromResult(language ?? "js");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureKatex : IKatexFormulaInterop
    {
        public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RenderAsync(
            ElementReference element,
            string formula,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureGoogle : IGoogleSearchInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference button,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new FixtureJsReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureSparkle : ISparkleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference content,
            DotNetObjectReference<MkSparkle> receiver,
            long generation,
            bool animationEnabled,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new FixtureJsReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureJsReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => false;
    }
}
