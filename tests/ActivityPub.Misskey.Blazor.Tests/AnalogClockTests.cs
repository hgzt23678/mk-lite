using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class AnalogClockTests : BunitContext
{
    [Fact]
    public void DefaultClockPreservesThePinnedSvgDomAndAttributeFallthrough()
    {
        var interop = new RecordingAnalogClockInterop();
        Services.AddSingleton<IAnalogClockInterop>(interop);

        IRenderedComponent<MkAnalogClock> component = Render<MkAnalogClock>(parameters => parameters
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "analog-clock"));

        IElement root = component.Find("svg.mbcofsoe.fixture");
        Assert.Equal("0 0 10 10", root.GetAttribute("viewBox"));
        Assert.Equal("none", root.GetAttribute("preserveAspectRatio"));
        Assert.Equal("analog-clock", root.GetAttribute("data-contract"));
        Assert.Equal(12, component.FindAll("svg.mbcofsoe > circle").Count);
        IReadOnlyList<IElement> lines = component.FindAll("svg.mbcofsoe > line");
        Assert.Equal(3, lines.Count);
        Assert.Equal("s animate elastic", lines[0].ClassName);
        Assert.Equal("0.05", lines[0].GetAttribute("stroke-width"));
        Assert.Equal("0.1", lines[1].GetAttribute("stroke-width"));

        Attachment attached = Assert.Single(interop.Attachments);
        Assert.Equal(0.1, attached.Thickness);
        Assert.Null(attached.OffsetMinutes);
        Assert.False(attached.TwentyFourHour);
        Assert.Equal("dots", attached.Graduations);
        Assert.True(attached.FadeGraduations);
        Assert.Equal("elastic", attached.SecondHandAnimation);
    }

    [Fact]
    public void NumberAndNoGraduationBranchesKeepTheirExactShapes()
    {
        var interop = new RecordingAnalogClockInterop();
        Services.AddSingleton<IAnalogClockInterop>(interop);

        IRenderedComponent<MkAnalogClock> numbers = Render<MkAnalogClock>(parameters => parameters
            .Add(clock => clock.TwentyFourHour, true)
            .Add(clock => clock.Graduations, AnalogClockGraduations.Numbers)
            .Add(clock => clock.FadeGraduations, false)
            .Add(clock => clock.SecondHandAnimation, AnalogClockSecondHandAnimation.EaseOut)
            .Add(clock => clock.OffsetMinutes, 540d));

        IReadOnlyList<IElement> labels = numbers.FindAll("svg.mbcofsoe > text");
        Assert.Equal(24, labels.Count);
        Assert.Equal("24", labels[0].TextContent);
        Assert.Equal("23", labels[^1].TextContent);
        Assert.All(labels, label => Assert.Equal("1", label.GetAttribute("opacity")));
        Assert.Equal("s animate easeOut", numbers.Find("svg.mbcofsoe > line.s").ClassName);

        IRenderedComponent<MkAnalogClock> none = Render<MkAnalogClock>(parameters => parameters
            .Add(clock => clock.Graduations, AnalogClockGraduations.None)
            .Add(clock => clock.SecondHandAnimation, AnalogClockSecondHandAnimation.None));
        Assert.Empty(none.FindAll("svg.mbcofsoe > circle"));
        Assert.Empty(none.FindAll("svg.mbcofsoe > text"));
        Assert.Equal("s", none.Find("svg.mbcofsoe > line.s").ClassName);
    }

    [Fact]
    public async Task ParameterReplacementAndDisposalReleaseEveryBrowserTicker()
    {
        var interop = new RecordingAnalogClockInterop();
        Services.AddSingleton<IAnalogClockInterop>(interop);
        IRenderedComponent<MkAnalogClock> component = Render<MkAnalogClock>();
        RecordingHandle first = Assert.Single(interop.Handles);

        component.Render(parameters => parameters
            .Add(clock => clock.TwentyFourHour, true)
            .Add(clock => clock.Graduations, AnalogClockGraduations.Numbers));
        Assert.Equal(2, interop.Attachments.Count);
        Assert.Equal(1, first.DisposeCalls);
        Assert.True(first.ReferenceDisposed);

        RecordingHandle second = interop.Handles[1];
        await component.Instance.DisposeAsync();
        Assert.Equal(1, second.DisposeCalls);
        Assert.True(second.ReferenceDisposed);
    }

    private sealed record Attachment(
        double Thickness,
        double? OffsetMinutes,
        bool TwentyFourHour,
        string Graduations,
        bool FadeGraduations,
        string SecondHandAnimation);

    private sealed class RecordingAnalogClockInterop : IAnalogClockInterop
    {
        public List<Attachment> Attachments { get; } = [];

        public List<RecordingHandle> Handles { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            double thickness,
            double? offsetMinutes,
            bool twentyFourHour,
            string graduations,
            bool fadeGraduations,
            string secondHandAnimation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = element;
            Attachments.Add(new(
                thickness,
                offsetMinutes,
                twentyFourHour,
                graduations,
                fadeGraduations,
                secondHandAnimation));
            var handle = new RecordingHandle();
            Handles.Add(handle);
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeCalls { get; private set; }

        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "dispose")
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
