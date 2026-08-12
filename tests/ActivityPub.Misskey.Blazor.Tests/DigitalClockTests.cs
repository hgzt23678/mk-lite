using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class DigitalClockTests : BunitContext
{
    [Fact]
    public void PreservesThePinnedDefaultDomAndAttributeFallthrough()
    {
        var interop = new RecordingDigitalClockInterop();
        Services.AddSingleton<IDigitalClockInterop>(interop);

        IRenderedComponent<MkDigitalClock> component = Render<MkDigitalClock>(parameters => parameters
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "digital-clock"));

        IElement root = component.Find("span.zjobosdg.fixture");
        Assert.Equal("digital-clock", root.GetAttribute("data-contract"));
        Assert.Equal([null, "colon", null, "colon", null], root.Children.Select(child => child.ClassName));
        Assert.Equal(2, component.FindAll("span.zjobosdg > .colon").Count);
        Assert.Single(interop.Attachments);
        Assert.True(interop.Attachments[0].ShowSeconds);
        Assert.False(interop.Attachments[0].ShowMilliseconds);
        Assert.Null(interop.Attachments[0].OffsetMinutes);
    }

    [Fact]
    public void ShowFlagsPreserveTheConditionalSpanOrder()
    {
        var interop = new RecordingDigitalClockInterop();
        Services.AddSingleton<IDigitalClockInterop>(interop);

        IRenderedComponent<MkDigitalClock> component = Render<MkDigitalClock>(parameters => parameters
            .Add(clock => clock.ShowSeconds, false)
            .Add(clock => clock.ShowMilliseconds, true)
            .Add(clock => clock.OffsetMinutes, 540));

        Assert.Equal(
            [null, "colon", null, "colon", null],
            component.Find("span.zjobosdg").Children.Select(child => child.ClassName));
        Assert.Equal(540, interop.Attachments.Single().OffsetMinutes);
        Assert.False(interop.Attachments.Single().ShowSeconds);
        Assert.True(interop.Attachments.Single().ShowMilliseconds);
    }

    [Fact]
    public async Task ParameterChangesReplaceAndDisposeTheBrowserTicker()
    {
        var interop = new RecordingDigitalClockInterop();
        Services.AddSingleton<IDigitalClockInterop>(interop);
        IRenderedComponent<MkDigitalClock> component = Render<MkDigitalClock>();
        RecordingHandle first = interop.Handles.Single();

        component.Render(parameters => parameters.Add(clock => clock.ShowMilliseconds, true));
        Assert.Equal(2, interop.Attachments.Count);
        Assert.Equal(1, first.DisposeCalls);
        Assert.True(first.ReferenceDisposed);

        RecordingHandle second = interop.Handles[1];
        await component.Instance.DisposeAsync();
        Assert.Equal(1, second.DisposeCalls);
        Assert.True(second.ReferenceDisposed);
    }

    private sealed record Attachment(bool ShowSeconds, bool ShowMilliseconds, int? OffsetMinutes);

    private sealed class RecordingDigitalClockInterop : IDigitalClockInterop
    {
        public List<Attachment> Attachments { get; } = [];

        public List<RecordingHandle> Handles { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool showSeconds,
            bool showMilliseconds,
            int? offsetMinutes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = element;
            Attachments.Add(new Attachment(showSeconds, showMilliseconds, offsetMinutes));
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
