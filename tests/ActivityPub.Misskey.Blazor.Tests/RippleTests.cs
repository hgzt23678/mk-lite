using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class RippleTests : BunitContext
{
    private static readonly string[] ParticleColors = ["#FF1493", "#00FFFF", "#FFE202"];

    [Fact]
    public void ParticleVariantPreservesSvgDomGeometryAndFallthroughContract()
    {
        var interop = new RecordingRippleInterop();
        Services.AddSingleton<IRippleEffectInterop>(interop);

        IRenderedComponent<MkRipple> component = Render<MkRipple>(parameters => parameters
            .Add(ripple => ripple.X, 160)
            .Add(ripple => ripple.Y, 120)
            .Add(ripple => ripple.Particle, true)
            .AddUnmatched("class", "fixture")
            .AddUnmatched("style", "opacity: 0.75;")
            .AddUnmatched("data-fixture", "ripple"));

        IElement root = component.Find("div.vswabwbm.fixture");
        Assert.Equal("top: 56px; left: 96px; opacity: 0.75;", root.GetAttribute("style"));
        Assert.Equal("true", root.GetAttribute("aria-hidden"));
        Assert.Equal("ripple", root.GetAttribute("data-fixture"));
        Assert.Equal("128", root.QuerySelector("svg")?.GetAttribute("width"));
        Assert.Equal("128", root.QuerySelector("svg")?.GetAttribute("height"));
        Assert.Equal("0 0 128 128", root.QuerySelector("svg")?.GetAttribute("viewBox"));
        Assert.Equal(13, root.QuerySelectorAll("circle").Length);
        Assert.Equal(38, root.QuerySelectorAll("animate").Length);
        Assert.Equal(12, root.QuerySelectorAll("g > circle").Length);
        Assert.All(root.QuerySelectorAll("g > circle"), particle =>
            Assert.Contains(particle.GetAttribute("fill"), ParticleColors));
        component.WaitForAssertion(() => Assert.True(interop.Attached));
    }

    [Fact]
    public void ParticleFalsePreservesRingAndOmitsParticleCircles()
    {
        Services.AddSingleton<IRippleEffectInterop>(new RecordingRippleInterop());

        IRenderedComponent<MkRipple> component = Render<MkRipple>(parameters => parameters
            .Add(ripple => ripple.Particle, false));

        Assert.Single(component.FindAll("svg > circle"));
        Assert.Empty(component.FindAll("svg > g > circle"));
        Assert.Equal(2, component.FindAll("animate").Count);
    }

    [Fact]
    public async Task EndIsEmittedOnceAndDisposalReleasesJavascriptHandle()
    {
        var interop = new RecordingRippleInterop();
        Services.AddSingleton<IRippleEffectInterop>(interop);
        int ends = 0;
        IRenderedComponent<MkRipple> component = Render<MkRipple>(parameters => parameters
            .Add(ripple => ripple.End, () => ends++));
        component.WaitForAssertion(() => Assert.True(interop.Attached));

        await component.Instance.NotifyEnded();
        await component.Instance.NotifyEnded();
        await component.Instance.DisposeAsync();

        Assert.Equal(1, ends);
        Assert.Contains("dispose", interop.Reference.Invocations);
        Assert.True(interop.Reference.Disposed);
    }

    private sealed class RecordingRippleInterop : IRippleEffectInterop
    {
        public RecordingReference Reference { get; } = new();

        public bool Attached { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(Reference);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingReference : IJSObjectReference
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
