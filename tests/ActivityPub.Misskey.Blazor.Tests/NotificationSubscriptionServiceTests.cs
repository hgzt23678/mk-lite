using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Streaming;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotificationSubscriptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnlyAuthenticatedRecipientAndUnmutedTypeProduceCreatedMutation()
    {
        UserNotification foreign = Notification("https://local.example/users/mallory", "foreign");
        UserNotification expected = Notification("https://local.example/users/alice", "expected");
        var presentation = new RecordingPresentation(expected.Id, View(expected.Id));
        var service = new NotificationSubscriptionService(
            new FinitePump(
            [
                StreamEvent.FromNotification(foreign, (Domain.Visibility)Visibility.MentionedOnly, isLocal: false),
                StreamEvent.FromNotification(expected, (Domain.Visibility)Visibility.MentionedOnly, isLocal: false)
            ]),
            new FixedStore(new([], 1, 2, RequestedCursorExpired: false)),
            new FixedActorContext(),
            presentation,
            new StreamingOptions());

        var mutations = new List<NotificationMutation>();
        await foreach (NotificationMutation mutation in service.SubscribeAsync(
                           2,
                           includeTypes: null,
                           excludeTypes: new HashSet<MisskeyNotificationType> { MisskeyNotificationType.Follow },
                           CancellationToken.None))
        {
            mutations.Add(mutation);
        }

        Assert.Equal(2, mutations.Count);
        Assert.Equal(NotificationMutationKind.Checkpoint, mutations[0].Kind);
        Assert.Equal(NotificationMutationKind.Created, mutations[1].Kind);
        Assert.Equal(expected.Id, mutations[1].Notification?.InternalId);
        Assert.Equal(1, presentation.FindCalls);
    }

    [Fact]
    public async Task MutedNotificationAdvancesCursorWithoutLeakingProjection()
    {
        UserNotification expected = Notification("https://local.example/users/alice", "muted");
        var presentation = new RecordingPresentation(expected.Id, View(expected.Id));
        var service = new NotificationSubscriptionService(
            new FinitePump([StreamEvent.FromNotification(expected, (Domain.Visibility)Visibility.MentionedOnly, isLocal: false)]),
            new FixedStore(new([], 1, 2, RequestedCursorExpired: false)),
            new FixedActorContext(),
            presentation,
            new StreamingOptions());

        NotificationMutation mutation = Assert.Single(await CollectAsync(service.SubscribeAsync(
            2,
            includeTypes: null,
            excludeTypes: new HashSet<MisskeyNotificationType> { MisskeyNotificationType.Reaction },
            CancellationToken.None)));

        Assert.Equal(NotificationMutationKind.Checkpoint, mutation.Kind);
        Assert.Null(mutation.Notification);
    }

    [Fact]
    public async Task ExpiredCursorIsRejectedBeforeOpeningPump()
    {
        var pump = new FinitePump([]);
        var service = new NotificationSubscriptionService(
            pump,
            new FixedStore(new([], 50, 80, RequestedCursorExpired: true)),
            new FixedActorContext(),
            new RecordingPresentation(Guid.NewGuid(), null),
            new StreamingOptions());

        NotificationCursorException exception = await Assert.ThrowsAsync<NotificationCursorException>(async () =>
            await CollectAsync(service.SubscribeAsync(2, null, null, CancellationToken.None)));

        Assert.Equal("STREAM_CURSOR_EXPIRED", exception.ErrorCode);
        Assert.Equal(0, pump.Subscriptions);
    }

    private static UserNotification Notification(string recipient, string suffix) => UserNotification.Create(
        recipient,
        "https://remote.example/users/bob",
        UserNotificationKind.Reaction,
        $"https://remote.example/activities/{suffix}",
        "https://remote.example/objects/note",
        "🎉",
        Now);

    private static NotificationViewModel View(Guid id) => new(
        id,
        "9notification",
        Now,
        MisskeyNotificationType.Reaction,
        IsRead: false,
        User: null,
        Note: null,
        Reaction: "🎉");

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (T item in source)
        {
            result.Add(item);
        }

        return result;
    }

    private sealed class RecordingPresentation(Guid id, NotificationViewModel? value) : INotificationPresentationService
    {
        public int FindCalls { get; private set; }

        public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
            NotificationPresentationQuery request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult(notificationId == id ? value : null);
        }

        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> MarkAllReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedActorContext : IAuthenticatedActorContext
    {
        private static readonly AuthenticatedActor Actor = new("alice", "https://local.example/users/alice");

        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedActor?>(Actor);

        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) => Task.FromResult(Actor);
        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
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
            _ = afterCursor;
            _ = bufferCapacity;
            _ = pollInterval;
            Subscriptions++;
            foreach (StreamEvent item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FixedStore(StreamEventPage page) : IStreamEventStore
    {
        public Task<StreamEventPage> ReadAfterAsync(
            long afterCursor,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(page);
        }
    }
}
