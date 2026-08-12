using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormRangeTests : BunitContext
{
    [Fact]
    public async Task RangePreservesTicksStepRoundingSlotsAndDeferredModelUpdate()
    {
        var interop = new RecordingRangeInterop();
        Services.AddSingleton<IFormRangeInterop>(interop);
        double value = 2;
        IRenderedComponent<MkFormRange> component = Render<MkFormRange>(parameters => parameters
            .Add(range => range.Value, value)
            .Add(range => range.ValueChanged, next => value = next)
            .Add(range => range.Min, 0)
            .Add(range => range.Max, 10)
            .Add(range => range.Step, 2)
            .Add(range => range.ShowTicks, true)
            .Add(range => range.Easing, true)
            .Add(range => range.Disabled, true)
            .Add(range => range.Label, builder => builder.AddContent(0, "音量"))
            .Add(range => range.Caption, builder => builder.AddContent(0, "再生音量"))
            .AddUnmatched("class", "fixture-range")
            .AddUnmatched("data-range-id", "volume"));

        AngleSharp.Dom.IElement root = component.Find(".timctyfi.disabled.easing.fixture-range[data-range-id=volume]");
        Assert.Equal("音量", root.QuerySelector(":scope > .label")?.TextContent);
        Assert.Equal("再生音量", root.QuerySelector(":scope > .caption")?.TextContent);
        Assert.Equal("width: 20%;", root.QuerySelector(".track > .highlight")?.GetAttribute("style"));
        Assert.Equal(6, component.FindAll(".ticks > .tick").Count);
        Assert.Equal("left: 0%;", component.FindAll(".ticks > .tick")[0].GetAttribute("style"));
        Assert.Equal("left: 100%;", component.FindAll(".ticks > .tick")[5].GetAttribute("style"));
        component.WaitForAssertion(() => Assert.Equal(0.2, interop.InitialValue, precision: 8));

        await component.InvokeAsync(() => component.Instance.NotifyRawValue(0.74));
        Assert.Equal("width: 80%;", component.Find(".track > .highlight").GetAttribute("style"));
        Assert.Equal(2, value);

        await component.InvokeAsync(() => component.Instance.NotifyDragEnded(0.74, changed: true));

        Assert.Equal(8, value);
    }

    [Fact]
    public async Task RangePreservesFractionalStepWithoutIntegerRounding()
    {
        Services.AddSingleton<IFormRangeInterop>(new RecordingRangeInterop());
        double value = 0;
        IRenderedComponent<MkFormRange> component = Render<MkFormRange>(parameters => parameters
            .Add(range => range.Value, value)
            .Add(range => range.ValueChanged, next => value = next)
            .Add(range => range.Min, 0)
            .Add(range => range.Max, 1)
            .Add(range => range.Step, 0.25));

        await component.InvokeAsync(() => component.Instance.NotifyDragEnded(0.62, changed: true));

        Assert.Equal(0.5, value, precision: 8);
    }

    private sealed class RecordingRangeInterop : IFormRangeInterop, IDisposable
    {
        public double InitialValue { get; private set; } = double.NaN;

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference container,
            ElementReference thumb,
            ElementReference highlight,
            double normalizedValue,
            DotNetObjectReference<MkFormRange> receiver,
            CancellationToken cancellationToken)
        {
            InitialValue = normalizedValue;
            return ValueTask.FromResult<IJSObjectReference>(new NoopReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NoopReference : IJSObjectReference
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
