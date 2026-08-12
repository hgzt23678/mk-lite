using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TimelineTutorialTests : BunitContext
{
    public TimelineTutorialTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new TutorialLocalizer());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonRippleInterop());
    }

    [Fact]
    public void PreservesPinnedSevenStepNavigationAndCompletionContract()
    {
        int? changed = null;
        using IRenderedComponent<TimelineTutorial> component = Render<TimelineTutorial>(parameters => parameters
            .Add(tutorial => tutorial.Step, 0)
            .Add(tutorial => tutorial.StepChanged, value => changed = value)
            .AddUnmatched("class", "tutorial _block")
            .AddUnmatched("data-fixture", "timeline-tutorial"));

        Assert.Contains("_card", component.Find(".tbkwesmv").ClassList);
        Assert.Contains("tutorial", component.Find(".tbkwesmv").ClassList);
        Assert.Contains("_block", component.Find(".tbkwesmv").ClassList);
        Assert.Equal("timeline-tutorial", component.Find(".tbkwesmv").GetAttribute("data-fixture"));
        Assert.Equal(3, component.FindAll(".tbkwesmv > ._content > div").Count);
        Assert.True(component.Find(".navigation .arrow:first-child").HasAttribute("disabled"));
        Assert.Equal("1 / 7", component.Find(".navigation .step > span").TextContent);

        component.Find(".navigation .ok").Click();
        Assert.Equal(1, changed);

        component.Render(parameters => parameters
            .Add(tutorial => tutorial.Step, 6)
            .Add(tutorial => tutorial.StepChanged, value => changed = value));
        Assert.True(component.Find(".navigation .arrow:last-child").HasAttribute("disabled"));
        Assert.Equal("7 / 7", component.Find(".navigation .step > span").TextContent);
        Assert.Contains("わかった", component.Find(".navigation .ok").TextContent, StringComparison.Ordinal);

        component.Find(".navigation .ok").Click();
        Assert.Equal(-1, changed);
    }

    [Fact]
    public void ProjectsLocalizedSlotLinksAsSafeElementsInSourceOrder()
    {
        using IRenderedComponent<TimelineTutorial> discovery = Render<TimelineTutorial>(parameters => parameters
            .Add(tutorial => tutorial.Step, 4));

        IReadOnlyList<AngleSharp.Dom.IElement> links = discovery.FindAll(".tbkwesmv > ._content > div > a._link");
        Assert.Collection(
            links,
            featured =>
            {
                Assert.Equal("注目", featured.TextContent);
                Assert.Equal("/featured", featured.GetAttribute("href"));
            },
            explore =>
            {
                Assert.Equal("みつける", explore.TextContent);
                Assert.Equal("/explore", explore.GetAttribute("href"));
            });
        Assert.DoesNotContain("{featured}", discovery.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("{explore}", discovery.Markup, StringComparison.Ordinal);

        using IRenderedComponent<TimelineTutorial> help = Render<TimelineTutorial>(parameters => parameters
            .Add(tutorial => tutorial.Step, 6));
        AngleSharp.Dom.IElement helpLink = help.Find(".tbkwesmv > ._content > div > a._link");
        Assert.Equal("https://misskey-hub.net/help.html", helpLink.GetAttribute("href"));
        Assert.Equal("_blank", helpLink.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", helpLink.GetAttribute("rel"));
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TutorialLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["_tutorial.title"] = "チュートリアル",
            ["_tutorial.step1_1"] = "一つ目",
            ["_tutorial.step1_2"] = "二つ目",
            ["_tutorial.step1_3"] = "三つ目",
            ["_tutorial.step5_1"] = "探してみましょう。",
            ["_tutorial.step5_2"] = "{featured} と {explore} を利用できます。",
            ["_tutorial.step5_3"] = "フォローしましょう。",
            ["_tutorial.step5_4"] = "補足",
            ["_tutorial.step7_1"] = "最後です。",
            ["_tutorial.step7_2"] = "詳しくは {help} を確認してください。",
            ["_tutorial.step7_3"] = "楽しんでください。",
            ["featured"] = "注目",
            ["explore"] = "みつける",
            ["help"] = "ヘルプ",
            ["goBack"] = "戻る",
            ["next"] = "次へ",
            ["gotIt"] = "わかった"
        };

        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Values.TryGetValue(key, out string? value) ? value : key;

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
