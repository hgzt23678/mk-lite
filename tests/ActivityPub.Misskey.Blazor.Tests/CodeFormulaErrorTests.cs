using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class CodeFormulaErrorTests : BunitContext
{
    private readonly RecordingPrismInterop prism = new();
    private readonly RecordingKatexInterop katex = new();

    public CodeFormulaErrorTests()
    {
        Services.AddSingleton<IPrismSyntaxHighlightInterop>(prism);
        Services.AddSingleton<IKatexFormulaInterop>(katex);
        Services.AddSingleton<IMisskeyLocalizer>(new FixtureLocalizer());
    }

    [Fact]
    public void CodePreservesPinnedBlockInlineFallbackAndLazyHighlightContracts()
    {
        using IRenderedComponent<MkCode> block = Render<MkCode>(parameters => parameters
            .Add(component => component.Code, "const answer = 42;")
            .Add(component => component.Lang, "javascript")
            .AddUnmatched("class", "fixture-code"));

        block.WaitForAssertion(() =>
        {
            Assert.True(prism.LoadCalls >= 1);
            Assert.Equal("language-javascript fixture-code", block.Find("pre").ClassName);
            Assert.Equal("language-javascript", block.Find("pre > code").ClassName);
            Assert.Equal("const answer = 42;", block.Find("pre > code").TextContent);
            Assert.Contains(prism.Highlights, item =>
                item.Code == "const answer = 42;" && item.Language == "javascript");
        });

        using IRenderedComponent<MkCode> inline = Render<MkCode>(parameters => parameters
            .Add(component => component.Code, "<unsafe>")
            .Add(component => component.Lang, "not-loaded")
            .Add(component => component.Inline, true));

        inline.WaitForAssertion(() =>
        {
            Assert.Equal("language-js", inline.Find("code").ClassName);
            Assert.Equal("<unsafe>", inline.Find("code").TextContent);
            Assert.Contains("&lt;unsafe&gt;", inline.Markup, StringComparison.Ordinal);
            Assert.Contains(prism.Highlights, item =>
                item.Code == "<unsafe>" && item.Language == "not-loaded");
        });
    }

    [Fact]
    public void FormulaPreservesPinnedBlockAndInlineRootsAndLazyKatexBoundary()
    {
        using IRenderedComponent<MkFormula> block = Render<MkFormula>(parameters => parameters
            .Add(component => component.Formula, @"x^2 + y^2")
            .Add(component => component.Block, true)
            .AddUnmatched("class", "fixture-formula"));

        block.WaitForAssertion(() =>
        {
            Assert.True(katex.LoadCalls >= 1);
            Assert.NotNull(block.Find("div.fixture-formula"));
            Assert.Contains(@"x^2 + y^2", katex.Formulas);
        });

        using IRenderedComponent<MkFormula> inline = Render<MkFormula>(parameters => parameters
            .Add(component => component.Formula, @"\frac{1}{2}")
            .Add(component => component.Block, false));

        inline.WaitForAssertion(() =>
        {
            Assert.NotNull(inline.Find("span"));
            Assert.Contains(@"\frac{1}{2}", katex.Formulas);
        });
    }

    [Fact]
    public void ErrorPreservesPinnedDomZoomMotionAndRetryEmission()
    {
        var motion = new RecordingErrorAppearInterop();
        Services.AddSingleton<IErrorAppearInterop>(motion);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: true));
        Services.AddSingleton<IButtonRippleInterop>(new RecordingButtonRippleInterop());
        int retryCount = 0;

        using IRenderedComponent<MkError> component = Render<MkError>(parameters => parameters
            .Add(error => error.Retry, () => retryCount++));

        component.WaitForAssertion(() =>
        {
            Assert.True(motion.Attached);
            Assert.True(motion.Animate);
            Assert.Contains("zoom-enter-active", component.Find(".mjndxjcg").ClassList);
            Assert.Equal("/client-assets/about-icon.png", component.Find(".mjndxjcg > img._ghost").GetAttribute("src"));
            Assert.Equal("問題が発生しました", component.Find(".mjndxjcg > p").TextContent.Trim());
            Assert.Equal("再試行", component.Find(".mjndxjcg > .button .content").TextContent);
        });

        component.Find(".mjndxjcg > .button").Click();
        Assert.Equal(1, retryCount);
    }

    [Fact]
    public void NotFoundPreservesPinnedBodyAndLocalizedMetadataContent()
    {
        using IRenderedComponent<NotFoundPage> component = Render<NotFoundPage>();

        Assert.Equal(
            "指定されたURLに該当するページはありませんでした。",
            component.Find(".ipledcug > ._fullinfo > div").TextContent);
        Assert.Equal(
            "/client-assets/about-icon.png",
            component.Find(".ipledcug > ._fullinfo > img._ghost").GetAttribute("src"));
        Assert.DoesNotContain("mk-page-error", component.Markup, StringComparison.Ordinal);
    }

    private sealed class RecordingPrismInterop : IPrismSyntaxHighlightInterop
    {
        public int LoadCalls { get; private set; }
        public List<(string Code, string? Language)> Highlights { get; } = [];

        public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> HighlightAsync(
            ElementReference element,
            string code,
            string? language,
            CancellationToken cancellationToken)
        {
            Highlights.Add((code, language));
            return ValueTask.FromResult(language ?? "js");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingKatexInterop : IKatexFormulaInterop
    {
        public int LoadCalls { get; private set; }
        public List<string> Formulas { get; } = [];

        public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask RenderAsync(
            ElementReference element,
            string formula,
            CancellationToken cancellationToken)
        {
            Formulas.Add(formula);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingErrorAppearInterop : IErrorAppearInterop
    {
        public bool Attached { get; private set; }
        public bool Animate { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool animate,
            CancellationToken cancellationToken)
        {
            Attached = true;
            Animate = animate;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((T)(object)animation);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "somethingHappened" => "問題が発生しました",
            "retry" => "再試行",
            "notFound" => "見つかりません",
            "notFoundDescription" => "指定されたURLに該当するページはありませんでした。",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
