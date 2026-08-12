using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SpacerTests : BunitContext
{
    [Fact]
    public void StartsAtThePinnedZeroMarginAndFallsThroughRootAttributes()
    {
        var interop = new RecordingSpacerInterop();
        Services.AddSingleton<ISpacerInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(null));

        IRenderedComponent<MkSpacer> component = Render<MkSpacer>(parameters => parameters
            .Add(spacer => spacer.ContentMax, 0)
            .Add(spacer => spacer.AdditionalAttributes, new Dictionary<string, object>
            {
                ["class"] = "host-class",
                ["style"] = "color: rgb(1, 2, 3);",
                ["data-contract"] = "spacer"
            })
            .AddChildContent("content"));

        component.WaitForAssertion(() => Assert.Equal(1, interop.ObserveCalls));
        IElement root = component.Find("._root_b6w6v_1");
        Assert.Contains("host-class", root.ClassList);
        Assert.Equal("spacer", root.GetAttribute("data-contract"));
        Assert.Contains("padding: 0px", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("color: rgb(1, 2, 3)", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Null(component.Find("._content_b6w6v_6").GetAttribute("style"));
    }

    [Fact]
    public async Task PreservesThePinnedWidthDeviceAndContentMaxRules()
    {
        var interop = new RecordingSpacerInterop();
        Services.AddSingleton<ISpacerInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState("tablet"));

        IRenderedComponent<MkSpacer> component = Render<MkSpacer>(parameters => parameters
            .Add(spacer => spacer.ContentMax, 600)
            .Add(spacer => spacer.MarginMin, 20)
            .Add(spacer => spacer.MarginMax, 32));

        component.WaitForAssertion(() => Assert.Equal(1, interop.ObserveCalls));
        Assert.Equal("tablet", interop.Options?.OverriddenDeviceKind);
        Assert.Equal("max-width: 600px;", component.Find("._content_b6w6v_6").GetAttribute("style"));

        await component.InvokeAsync(() => component.Instance.UpdateSpacer(350, 500, "desktop"));
        Assert.Contains("padding: 20px", component.Find("._root_b6w6v_1").GetAttribute("style"), StringComparison.Ordinal);

        await component.InvokeAsync(() => component.Instance.UpdateSpacer(361, 401, "desktop"));
        Assert.Contains("padding: 32px", component.Find("._root_b6w6v_1").GetAttribute("style"), StringComparison.Ordinal);

        await component.InvokeAsync(() => component.Instance.UpdateSpacer(1000, 1200, "smartphone"));
        Assert.Contains("padding: 20px", component.Find("._root_b6w6v_1").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CascadedShouldSpacerMinOverridesDesktopWidth()
    {
        var interop = new RecordingSpacerInterop();
        Services.AddSingleton<ISpacerInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(null));

        IRenderedComponent<CascadingValue<bool>> host = Render<CascadingValue<bool>>(parameters => parameters
            .Add(value => value.Name, "ShouldSpacerMin")
            .Add(value => value.Value, true)
            .Add(value => value.ChildContent, builder =>
            {
                builder.OpenComponent<MkSpacer>(0);
                builder.AddAttribute(1, nameof(MkSpacer.MarginMin), 12);
                builder.AddAttribute(2, nameof(MkSpacer.MarginMax), 24);
                builder.CloseComponent();
            }));

        IRenderedComponent<MkSpacer> component = host.FindComponent<MkSpacer>();
        component.WaitForAssertion(() => Assert.Equal(1, interop.ObserveCalls));
        await component.InvokeAsync(() => component.Instance.UpdateSpacer(1000, 1200, "desktop"));
        Assert.Contains("padding: 12px", component.Find("._root_b6w6v_1").GetAttribute("style"), StringComparison.Ordinal);
    }

    private sealed class FixedDeviceState(string? overriddenDeviceKind) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("overridedDeviceKind", propertyName);
            return ValueTask.FromResult(overriddenDeviceKind is T value ? value : fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSpacerInterop : ISpacerInterop, IDisposable
    {
        public int ObserveCalls { get; private set; }

        public SpacerObservationOptions? Options { get; private set; }

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            ObserveCalls++;
            Options = options;
            return ValueTask.FromResult<IJSObjectReference>(new ObservationHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class ObservationHandle : IJSObjectReference
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
