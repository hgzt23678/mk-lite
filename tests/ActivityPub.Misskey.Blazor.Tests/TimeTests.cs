using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TimeTests : BunitContext
{
    private static readonly DateTimeOffset Value = new(2026, 8, 4, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task RelativeModeMatchesUpstreamThresholdsAndAttributeFallthrough()
    {
        RecordingTimeInterop interop = RegisterDependencies("en-US");
        IRenderedComponent<MkTime> component = Render<MkTime>(parameters => parameters
            .Add(time => time.Time, Value)
            .AddUnmatched("class", "created-at")
            .AddUnmatched("data-contract", "time"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.InvokeAsync(() => interop.Receiver!.UpdateTime(
            interop.Generation,
            Value.AddSeconds(70).ToUnixTimeMilliseconds(),
            "8/4/2026, 12:34:56 PM"));

        IElement time = component.Find("time.created-at");
        Assert.Equal("1 minute(s) ago", time.TextContent);
        Assert.Equal("8/4/2026, 12:34:56 PM", time.GetAttribute("title"));
        Assert.Equal("time", time.GetAttribute("data-contract"));
        Assert.Null(time.GetAttribute("datetime"));
        Assert.True(interop.UpdateRelativeTime);
    }

    [Theory]
    [InlineData(-2, "Future")]
    [InlineData(0, "Just now")]
    [InlineData(50, "50 second(s) ago")]
    [InlineData(90, "1 minute(s) ago")]
    [InlineData(5_400, "2 hour(s) ago")]
    [InlineData(47_520_000, "2 year(s) ago")]
    public async Task RelativeModePreservesEveryVueBoundary(long seconds, string expected)
    {
        RecordingTimeInterop interop = RegisterDependencies("en-US");
        IRenderedComponent<MkTime> component = Render<MkTime>(parameters => parameters
            .Add(time => time.Time, Value));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.InvokeAsync(() => interop.Receiver!.UpdateTime(
            interop.Generation,
            Value.AddSeconds(seconds).ToUnixTimeMilliseconds(),
            "absolute"));

        Assert.Equal(expected, component.Find("time").TextContent);
    }

    [Fact]
    public async Task AbsoluteAndDetailModesUseBrowserTextAndReattachOnModeChange()
    {
        RecordingTimeInterop interop = RegisterDependencies("ja-JP");
        IRenderedComponent<MkTime> component = Render<MkTime>(parameters => parameters
            .Add(time => time.Time, Value)
            .Add(time => time.Mode, "absolute"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));
        await component.InvokeAsync(() => interop.Receiver!.UpdateTime(
            interop.Generation,
            Value.AddSeconds(70).ToUnixTimeMilliseconds(),
            "2026/8/4 12:34:56"));
        Assert.Equal("2026/8/4 12:34:56", component.Find("time").TextContent);
        Assert.False(interop.UpdateRelativeTime);
        long replacedGeneration = interop.Generation;

        component.Render(parameters => parameters
            .Add(time => time.Time, Value)
            .Add(time => time.Mode, "detail"));
        component.WaitForAssertion(() => Assert.Equal(2, interop.AttachCalls));
        await component.InvokeAsync(() => interop.Receiver!.UpdateTime(
            replacedGeneration,
            Value.AddYears(1).ToUnixTimeMilliseconds(),
            "stale callback"));
        Assert.Equal("2026/8/4 12:34:56", component.Find("time").GetAttribute("title"));
        await component.InvokeAsync(() => interop.Receiver!.UpdateTime(
            interop.Generation,
            Value.AddSeconds(70).ToUnixTimeMilliseconds(),
            "2026/8/4 12:34:56"));

        Assert.Equal("2026/8/4 12:34:56 (1分前)", component.Find("time").TextContent);
        Assert.True(interop.UpdateRelativeTime);
        Assert.Equal(1, interop.Handles[0].DisposeCalls);
        Assert.True(interop.Handles[0].ReferenceDisposed);
    }

    [Fact]
    public async Task InvalidModeFailsAndDisposalReleasesTimerObserverHandle()
    {
        RecordingTimeInterop interop = RegisterDependencies("en-US");
        Assert.Throws<ArgumentOutOfRangeException>(() => Render<MkTime>(parameters => parameters
            .Add(time => time.Time, Value)
            .Add(time => time.Mode, "unknown")));

        IRenderedComponent<MkTime> component = Render<MkTime>(parameters => parameters
            .Add(time => time.Time, Value));
        component.WaitForAssertion(() => Assert.Single(interop.Handles));
        await component.Instance.DisposeAsync();

        Assert.Equal(1, interop.Handles[0].DisposeCalls);
        Assert.True(interop.Handles[0].ReferenceDisposed);
    }

    private RecordingTimeInterop RegisterDependencies(string locale)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = locale;
        var catalog = new MisskeyLocaleCatalog();
        Services.AddSingleton<IMisskeyLocalizer>(new MisskeyLocalizer(
            catalog,
            new MisskeyLocaleRequestResolver(catalog),
            new HttpContextAccessor { HttpContext = context }));
        var interop = new RecordingTimeInterop();
        Services.AddSingleton<ITimeInterop>(interop);
        return interop;
    }

    private sealed class RecordingTimeInterop : ITimeInterop
    {
        public List<RecordingTimeHandle> Handles { get; } = [];

        public MkTime? Receiver { get; private set; }

        public bool UpdateRelativeTime { get; private set; }

        public int AttachCalls { get; private set; }

        public long Generation { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<MkTime> receiver,
            long generation,
            long unixTimeMilliseconds,
            bool updateRelativeTime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = element;
            Assert.Equal(Value.ToUnixTimeMilliseconds(), unixTimeMilliseconds);
            Receiver = receiver.Value;
            Generation = generation;
            UpdateRelativeTime = updateRelativeTime;
            AttachCalls++;
            var handle = new RecordingTimeHandle();
            Handles.Add(handle);
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingTimeHandle : IJSObjectReference
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
