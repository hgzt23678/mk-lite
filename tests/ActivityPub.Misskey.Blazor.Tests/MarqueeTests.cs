using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MarqueeTests : BunitContext
{
    [Fact]
    public void RepeatsTheSlotAndMeasuresUsingThePinnedMisskeyContract()
    {
        var browser = new RecordingMarqueeInterop();
        Services.AddSingleton<IMarqueeInterop>(browser);

        IRenderedComponent<MkMarquee> component = Render<MkMarquee>(parameters => parameters
            .Add(value => value.Duration, 40)
            .Add(value => value.Repeat, 2)
            .AddUnmatched("class", "fixture-marquee")
            .AddUnmatched("data-marquee-id", "federation")
            .AddChildContent("<a class=\"instance\">remote.example</a>"));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find("._wrap_1hc4p_1 > ._content_1hc4p_9"));
            Assert.Equal(2, component.FindAll("._content_1hc4p_9 > ._text_1hc4p_15").Count);
            Assert.Equal(2, component.FindAll("a.instance").Count);
            Assert.Contains("fixture-marquee", component.Find("._wrap_1hc4p_1").ClassList);
            Assert.Equal("federation", component.Find("._wrap_1hc4p_1").GetAttribute("data-marquee-id"));
            Assert.Null(component.FindAll("._text_1hc4p_15")[0].GetAttribute("aria-hidden"));
            Assert.Equal("true", component.FindAll("._text_1hc4p_15")[1].GetAttribute("aria-hidden"));
            Assert.Equal([(2, 40d)], browser.Measurements);
        });
    }

    [Fact]
    public void PreservesPausedReverseAndDurationUpdates()
    {
        var browser = new RecordingMarqueeInterop();
        Services.AddSingleton<IMarqueeInterop>(browser);
        IRenderedComponent<MkMarquee> component = Render<MkMarquee>(parameters => parameters
            .Add(value => value.Duration, 15)
            .Add(value => value.Paused, true)
            .Add(value => value.Reverse, true)
            .AddChildContent("federation"));

        component.WaitForAssertion(() => Assert.Single(browser.Measurements));
        Assert.Contains("_paused_1hc4p_24", component.Find("span._content_1hc4p_9").ClassList);
        Assert.All(
            component.FindAll("span._text_1hc4p_15"),
            element => Assert.Equal("animation-direction: reverse", element.GetAttribute("style")));

        component.Render(parameters => parameters.Add(value => value.Duration, 20));
        component.WaitForAssertion(() => Assert.Equal([(2, 15d), (2, 20d)], browser.Measurements));
    }

    private sealed class RecordingMarqueeInterop : IMarqueeInterop
    {
        public List<(int Repeat, double Duration)> Measurements { get; } = [];

        public ValueTask<double> SetDurationAsync(
            ElementReference content,
            int repeat,
            double duration,
            CancellationToken cancellationToken)
        {
            Measurements.Add((repeat, duration));
            return ValueTask.FromResult(duration);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
