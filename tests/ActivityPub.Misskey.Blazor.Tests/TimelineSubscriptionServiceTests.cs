using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Streaming;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TimelineSubscriptionServiceTests
{
    [Fact]
    public async Task EventRejectedByViewerPolicyAdvancesAsCheckpointWithoutProjectingContent()
    {
        StreamEvent item = CreateEvent(Visibility.MentionedOnly);
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/alice",
            CanReceiveStreamEvent = false,
            StreamPost = ClientViewFactory.Post("must not be projected")
        };
        var actorContext = new AuthenticatedActorContext(
            FixedAuthenticationStateProvider.Authenticated("alice"),
            query);
        var presentation = new RecordingTimelinePresentationService();
        var connectionStatus = new MisskeyStreamConnectionStatus();
        var service = new TimelineSubscriptionService(
            new FinitePump([item]),
            new FixedStreamStore(new([], 1, 1, RequestedCursorExpired: false)),
            query,
            actorContext,
            presentation,
            connectionStatus,
            new StreamingOptions());

        var mutations = new List<TimelineMutation>();
        await foreach (TimelineMutation mutation in service.SubscribeAsync(
                           TimelineKind.Home,
                           1,
                           CancellationToken.None))
        {
            mutations.Add(mutation);
        }

        TimelineMutation checkpoint = Assert.Single(mutations);
        Assert.Equal(TimelineMutationKind.Checkpoint, checkpoint.Kind);
        Assert.Equal(0, presentation.FindCalls);
        Assert.Equal(0, query.StreamPostReads);
        Assert.False(connectionStatus.IsDisconnected);
    }

    [Fact]
    public async Task ExpiredCursorIsReportedBeforeThePumpIsOpened()
    {
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var pump = new FinitePump([]);
        var connectionStatus = new MisskeyStreamConnectionStatus();
        var service = new TimelineSubscriptionService(
            pump,
            new FixedStreamStore(new([], 50, 80, RequestedCursorExpired: true)),
            query,
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query),
            new RecordingTimelinePresentationService(),
            connectionStatus,
            new StreamingOptions());

        TimelineCursorException exception = await Assert.ThrowsAsync<TimelineCursorException>(async () =>
        {
            await foreach (TimelineMutation _ in service.SubscribeAsync(
                               TimelineKind.Home,
                               2,
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("STREAM_CURSOR_EXPIRED", exception.ErrorCode);
        Assert.Equal(0, pump.Subscriptions);
        Assert.False(connectionStatus.IsDisconnected);
    }

    [Fact]
    public async Task MoveNextFailureReportsTheSharedDisconnectedState()
    {
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var connectionStatus = new MisskeyStreamConnectionStatus();
        int disconnected = 0;
        connectionStatus.Disconnected += (_, _) => disconnected++;
        var service = new TimelineSubscriptionService(
            new FailingPump(),
            new FixedStreamStore(new([], 1, 1, RequestedCursorExpired: false)),
            query,
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query),
            new RecordingTimelinePresentationService(),
            connectionStatus,
            new StreamingOptions());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (TimelineMutation _ in service.SubscribeAsync(
                               TimelineKind.Home,
                               1,
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("STREAM_TEST_DISCONNECTED", exception.Message);
        Assert.True(connectionStatus.IsDisconnected);
        Assert.Equal(1, disconnected);
    }

    private static StreamEvent CreateEvent(Visibility visibility)
    {
        DateTimeOffset now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        string actorIri = "https://remote.example/users/bob";
        string objectIri = "https://remote.example/objects/note";
        FederatedObject resource = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            visibility,
            "{\"type\":\"Note\"}",
            new string('a', 64),
            now,
            now);
        ActivityRecord activity = ActivityRecord.Create(
            "https://remote.example/activities/create",
            actorIri,
            "Create",
            objectIri,
            ActivityDirection.Inbound,
            visibility,
            "{\"type\":\"Create\"}",
            new string('b', 64),
            isTransient: false,
            now,
            now);
        return StreamEvent.FromObjectMutation(activity, resource, isLocal: false)!;
    }

    private sealed class FixedStreamStore(StreamEventPage page) : IStreamEventStore
    {
        public Task<StreamEventPage> ReadAfterAsync(long afterCursor, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(page);
    }

    private sealed class FinitePump(IReadOnlyList<StreamEvent> events) : IDurableStreamEventPump
    {
        public int Subscriptions { get; private set; }

        public async IAsyncEnumerable<StreamEvent> SubscribeAsync(
            long afterCursor,
            int bufferCapacity,
            TimeSpan pollInterval,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Subscriptions++;
            foreach (StreamEvent item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FailingPump : IDurableStreamEventPump
    {
        public async IAsyncEnumerable<StreamEvent> SubscribeAsync(
            long afterCursor,
            int bufferCapacity,
            TimeSpan pollInterval,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("STREAM_TEST_DISCONNECTED");
            }

            yield break;
        }
    }

    private sealed class RecordingTimelinePresentationService : ITimelinePresentationService
    {
        public int FindCalls { get; private set; }

        public Task<TimelinePageViewModel> ReadAsync(TimelineKind kind, string? beforeId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> VotePollAsync(string noteId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<NoteViewModel?>(null);
        }

        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
            Task.FromResult("note-" + id.ToString("N"));
    }
}
