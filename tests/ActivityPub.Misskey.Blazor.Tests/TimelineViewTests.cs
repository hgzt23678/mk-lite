using System.Globalization;
using System.Runtime.CompilerServices;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TimelineViewTests : BunitContext
{
    private readonly RecordingTimeline timeline = new();
    private readonly ControlledTimelineSubscription stream = new();

    public TimelineViewTests()
    {
        AddBunitPersistentComponentState();
        Services.AddSingleton<ITimelinePresentationService>(timeline);
        Services.AddSingleton<ITimelineSubscriptionService>(stream);
        Services.AddSingleton<IClientStorage>(new MemoryClientStorage());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IPaginationInterop>(new NoOpPaginationInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new TimelineLocalizer());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonRippleInterop());
        Services.AddSingleton<IErrorAppearInterop>(new NoOpErrorAppearInterop());
    }

    [Fact]
    public void UsesPinnedPaginationContractAndMisskeyEmptyState()
    {
        timeline.Responses.Enqueue(new TimelinePageViewModel([], null));

        using IRenderedComponent<TimelineView> component = Render<TimelineView>(parameters => parameters
            .Add(view => view.Kind, TimelineKind.Home)
            .AddUnmatched("class", "tl"));

        component.WaitForAssertion(() =>
        {
            Assert.Single(timeline.Reads);
            Assert.Equal((TimelineKind.Home, null, 11), timeline.Reads[0]);
            Assert.Equal("empty tl", component.Find(".empty").ClassName);
            Assert.Equal("/client-assets/about-icon.png", component.Find(".empty img._ghost").GetAttribute("src"));
            Assert.Equal("ノートはありません", component.Find(".empty ._fullinfo > div").TextContent);
            Assert.DoesNotContain("giivymft", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void KindReplacementCancelsTheOldStreamAndLoadsTheMatchingEndpointContract()
    {
        timeline.Responses.Enqueue(new TimelinePageViewModel([], null));
        timeline.Responses.Enqueue(new TimelinePageViewModel([], null));

        using IRenderedComponent<TimelineView> component = Render<TimelineView>(parameters => parameters
            .Add(view => view.Kind, TimelineKind.Home));

        component.WaitForAssertion(() =>
            Assert.Contains(stream.Subscriptions, subscription => subscription.Kind == TimelineKind.Home));

        component.Render(parameters => parameters
            .Add(view => view.Kind, TimelineKind.Local));

        component.WaitForAssertion(() =>
        {
            Assert.Collection(
                timeline.Reads,
                read => Assert.Equal((TimelineKind.Home, null, 11), read),
                read => Assert.Equal((TimelineKind.Local, null, 11), read));
            Assert.True(stream.Subscriptions.Single(item => item.Kind == TimelineKind.Home).Cancelled);
            Assert.Contains(stream.Subscriptions, subscription => subscription.Kind == TimelineKind.Local);
        });
    }

    [Fact]
    public void PagingFailureUsesMkErrorAndRetryDoesNotHideTheFailureBehindAnEmptyTimeline()
    {
        timeline.Failures.Enqueue(new InvalidOperationException("fixture failure"));
        timeline.Responses.Enqueue(new TimelinePageViewModel([], null));

        using IRenderedComponent<TimelineView> component = Render<TimelineView>();

        component.WaitForAssertion(() =>
            Assert.Equal("問題が発生しました", component.Find(".mjndxjcg > p").TextContent.Trim()));
        component.Find(".mjndxjcg > .button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, timeline.Reads.Count);
            Assert.NotNull(component.Find(".empty > ._fullinfo"));
        });
    }

    private sealed class RecordingTimeline : ITimelinePresentationService
    {
        public Queue<TimelinePageViewModel> Responses { get; } = [];
        public Queue<Exception> Failures { get; } = [];
        public List<(TimelineKind Kind, string? BeforeId, int Limit)> Reads { get; } = [];

        public Task<TimelinePageViewModel> ReadAsync(
            TimelineKind kind,
            string? beforeId,
            int limit,
            CancellationToken cancellationToken)
        {
            Reads.Add((kind, beforeId, limit));
            if (Failures.TryDequeue(out Exception? failure))
            {
                return Task.FromException<TimelinePageViewModel>(failure);
            }
            return Task.FromResult(Responses.Dequeue());
        }

        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> VotePollAsync(string noteId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<NoteViewModel?>(null);

        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
            Task.FromResult(id.ToString("N", CultureInfo.InvariantCulture));
    }

    private sealed class ControlledTimelineSubscription : ITimelineSubscriptionService
    {
        public List<Subscription> Subscriptions { get; } = [];

        public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) => Task.FromResult(41L);

        public async IAsyncEnumerable<TimelineMutation> SubscribeAsync(
            TimelineKind kind,
            long afterCursor,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = new Subscription(kind, afterCursor);
            Subscriptions.Add(subscription);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                subscription.Cancelled = cancellationToken.IsCancellationRequested;
            }

            yield break;
        }
    }

    private sealed class Subscription(TimelineKind kind, long afterCursor)
    {
        public TimelineKind Kind { get; } = kind;
        public long AfterCursor { get; } = afterCursor;
        public bool Cancelled { get; set; }
    }

    private sealed class MemoryClientStorage : IClientStorage
    {
        private readonly Dictionary<(ClientStorageArea Area, string Key), object?> values = [];

        public ValueTask<T?> ReadAsync<T>(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(values.TryGetValue((area, key), out object? value) ? (T?)value : default);

        public ValueTask WriteAsync<T>(
            ClientStorageArea area,
            string key,
            T value,
            CancellationToken cancellationToken = default)
        {
            values[(area, key)] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default)
        {
            values.Remove((area, key));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoOpPaginationInterop : IPaginationInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            bool enableAutoLoad,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> IsBottomVisibleAsync(ElementReference root, double tolerance, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, false, false));

        public ValueTask RestoreScrollAsync(
            ElementReference root,
            PaginationScrollSnapshot snapshot,
            bool stickToBottom,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpErrorAppearInterop : IErrorAppearInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool animate,
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

    private sealed class TimelineLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "noNotes" => "ノートはありません",
            "somethingHappened" => "問題が発生しました",
            "retry" => "再試行",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
