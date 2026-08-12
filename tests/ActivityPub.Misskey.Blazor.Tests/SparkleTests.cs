using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SparkleTests : BunitContext
{
    private static readonly string[] ParticleColors = ["#FF1493", "#00FFFF", "#FFE202"];

    [Fact]
    public async Task PreservesPinnedSvgParticleRangesAndAttributeFallthrough()
    {
        var interop = new RecordingSparkleInterop();
        Services.AddSingleton<ISparkleInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(true));
        IRenderedComponent<MkSparkle> component = Render<MkSparkle>(parameters => parameters
            .Add(sparkle => sparkle.ChildContent, "Misskey updated")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "sparkle"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.InvokeAsync(() => interop.Receiver!.UpdateSparkleMetrics(
            interop.Generation,
            100,
            20,
            reducedMotion: false));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("svg")));

        IElement root = component.Find("span.mk-sparkle.fixture");
        Assert.Equal("sparkle", root.GetAttribute("data-contract"));
        Assert.Equal("Misskey updated", root.QuerySelector(":scope > span")?.TextContent);
        IElement svg = component.Find("svg");
        Assert.Equal("164", svg.GetAttribute("width"));
        Assert.Equal("84", svg.GetAttribute("height"));
        Assert.Equal("0 0 164 84", svg.GetAttribute("viewBox"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        IElement path = Assert.IsAssignableFrom<IElement>(svg.QuerySelector("path"));
        string transform = Assert.IsType<string>(path.GetAttribute("transform"));
        double[] coordinates = transform["translate(".Length..^1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.InRange(coordinates[0], 0, 100);
        Assert.InRange(coordinates[1], 0, 20);
        Assert.Contains(path.GetAttribute("fill"), ParticleColors);
        IElement[] animations = svg.QuerySelectorAll("animateTransform").Cast<IElement>().ToArray();
        Assert.Equal(2, animations.Length);
        Assert.Equal("rotate", animations[0].GetAttribute("type"));
        Assert.Equal("scale", animations[1].GetAttribute("type"));
        Assert.All(animations, animation =>
        {
            Assert.Equal("1", animation.GetAttribute("repeatCount"));
            Assert.Equal("sum", animation.GetAttribute("additive"));
            double duration = double.Parse(
                Assert.IsType<string>(animation.GetAttribute("dur"))[..^2],
                CultureInfo.InvariantCulture);
            Assert.InRange(duration, 1_000, 2_000);
        });
        string[] scaleValues = Assert.IsType<string>(animations[1].GetAttribute("values")).Split(';');
        Assert.InRange(double.Parse(scaleValues[1], CultureInfo.InvariantCulture), 0.2, 0.5);
    }

    [Fact]
    public async Task ReducedMotionClearsParticlesAndCurrentGenerationCanResume()
    {
        var interop = new RecordingSparkleInterop();
        Services.AddSingleton<ISparkleInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(true));
        IRenderedComponent<MkSparkle> component = Render<MkSparkle>(parameters => parameters
            .Add(sparkle => sparkle.ChildContent, "Motion"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));
        await component.InvokeAsync(() => interop.Receiver!.UpdateSparkleMetrics(
            interop.Generation,
            80,
            20,
            reducedMotion: false));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("svg")));

        await component.InvokeAsync(() => interop.Receiver!.UpdateSparkleMetrics(
            interop.Generation,
            80,
            20,
            reducedMotion: true));
        Assert.Empty(component.FindAll("svg"));
        await component.InvokeAsync(() => interop.Receiver!.UpdateSparkleMetrics(
            interop.Generation - 1,
            80,
            20,
            reducedMotion: false));
        Assert.Empty(component.FindAll("svg"));
        await component.InvokeAsync(() => interop.Receiver!.UpdateSparkleMetrics(
            interop.Generation,
            80,
            20,
            reducedMotion: false));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("svg")));
    }

    [Fact]
    public async Task DeviceAnimationSettingAndDisposalReachTheTypedBrowserBoundary()
    {
        var interop = new RecordingSparkleInterop();
        Services.AddSingleton<ISparkleInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(false));
        IRenderedComponent<MkSparkle> component = Render<MkSparkle>(parameters => parameters
            .Add(sparkle => sparkle.ChildContent, "Static"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));
        Assert.False(interop.AnimationEnabled);

        await component.Instance.DisposeAsync();
        Assert.Equal(1, interop.Handle.DisposeCalls);
        Assert.True(interop.Handle.ReferenceDisposed);
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("animation", propertyName);
            return ValueTask.FromResult((T)(object)animation);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSparkleInterop : ISparkleInterop
    {
        public RecordingHandle Handle { get; } = new();

        public MkSparkle? Receiver { get; private set; }

        public long Generation { get; private set; }

        public bool AnimationEnabled { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference content,
            DotNetObjectReference<MkSparkle> receiver,
            long generation,
            bool animationEnabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = content;
            Receiver = receiver.Value;
            Generation = generation;
            AnimationEnabled = animationEnabled;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
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
