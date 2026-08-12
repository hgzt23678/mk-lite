using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UnixClockWidgetTests : BunitContext
{
    private readonly RecordingUnixClockInterop interop = new();

    public UnixClockWidgetTests()
    {
        Services.AddSingleton<IUnixClockInterop>(interop);
    }

    [Fact]
    public void RendersPinnedSurfaceInitialValuesAndAttachesWithMilliseconds()
    {
        var widget = new MisskeyWidgetModel { Name = "unixClock", Id = "w1" };

        using IRenderedComponent<MkwUnixClock> component = Render<MkwUnixClock>(parameters => parameters
            .Add(clock => clock.Widget, widget));

        component.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));
        Assert.True(interop.ShowMilliseconds);
        IElement root = component.Find(".mkw-unixClock._monospace._panel");
        Assert.Contains("font-size:", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal("UNIX Epoch", root.QuerySelector(":scope > .label")?.TextContent);
        IElement time = root.QuerySelector(":scope > .time")!;
        Assert.Equal(3, time.Children.Length);
        Assert.NotNull(time.QuerySelector(":scope > .colon"));
        Assert.NotEmpty(time.QuerySelector(":scope > span:first-child")?.TextContent ?? string.Empty);
    }

    [Fact]
    public void WidgetPropsControlMillisecondsLabelsAndTransparency()
    {
        Dictionary<string, JsonElement> data = new(StringComparer.Ordinal)
        {
            ["transparent"] = JsonSerializer.Deserialize<JsonElement>("true"),
            ["showMs"] = JsonSerializer.Deserialize<JsonElement>("false"),
            ["showLabel"] = JsonSerializer.Deserialize<JsonElement>("false"),
            ["fontSize"] = JsonSerializer.Deserialize<JsonElement>("2")
        };
        var widget = new MisskeyWidgetModel { Name = "unixClock", Id = "w1", Data = data };

        using IRenderedComponent<MkwUnixClock> component = Render<MkwUnixClock>(parameters => parameters
            .Add(clock => clock.Widget, widget));

        component.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));
        Assert.False(interop.ShowMilliseconds);
        IElement root = component.Find(".mkw-unixClock._monospace:not(._panel)");
        Assert.Contains("font-size: 2em;", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Empty(root.QuerySelectorAll(":scope > .label"));
        Assert.Equal(1, root.QuerySelector(":scope > .time")!.Children.Length);
    }

    private sealed class RecordingUnixClockInterop : IUnixClockInterop
    {
        public int AttachCalls { get; private set; }

        public bool ShowMilliseconds { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            bool showMilliseconds,
            CancellationToken cancellationToken)
        {
            _ = root;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            ShowMilliseconds = showMilliseconds;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
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
