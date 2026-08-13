using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Streaming;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class RelationshipSubscriptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 8, 30, 0, TimeSpan.Zero);
    private static readonly Guid TargetActorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task OnlyTheAuthenticatedRecipientAndTargetProduceAChange()
    {
        StreamEvent foreignRecipient = CreateEvent(
            "https://local.example/users/mallory",
            "https://remote.example/users/bob",
            TargetActorId,
            "foreign");
        StreamEvent expected = CreateEvent(
            "https://local.example/users/alice",
            "https://remote.example/users/bob",
            TargetActorId,
            "expected");
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var service = new RelationshipSubscriptionService(
            new FinitePump([foreignRecipient, expected]),
            new FixedStreamStore(new([], 1, 2, RequestedCursorExpired: false)),
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query),
            new StreamingOptions());

        var mutations = new List<RelationshipMutation>();
        await foreach (RelationshipMutation mutation in service.SubscribeAsync(
                           TargetActorId,
                           2,
                           CancellationToken.None))
        {
            mutations.Add(mutation);
        }

        Assert.Equal(2, mutations.Count);
        Assert.False(mutations[0].Changed);
        Assert.True(mutations[1].Changed);
    }

    [Fact]
    public async Task ExpiredCursorIsRejectedBeforeOpeningThePump()
    {
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var pump = new FinitePump([]);
        var service = new RelationshipSubscriptionService(
            pump,
            new FixedStreamStore(new([], 50, 80, RequestedCursorExpired: true)),
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query),
            new StreamingOptions());

        RelationshipCursorException exception = await Assert.ThrowsAsync<RelationshipCursorException>(async () =>
        {
            await foreach (RelationshipMutation _ in service.SubscribeAsync(
                               TargetActorId,
                               2,
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("STREAM_CURSOR_EXPIRED", exception.ErrorCode);
        Assert.Equal(0, pump.Subscriptions);
    }

    private static StreamEvent CreateEvent(
        string recipient,
        string target,
        Guid targetId,
        string suffix)
    {
        FollowRelation relationship = FollowRelation.Request(
            recipient,
            target,
            $"https://local.example/activities/follow-{suffix}",
            Now);
        ActivityRecord activity = ActivityRecord.Create(
            $"https://local.example/activities/follow-{suffix}",
            recipient,
            "Follow",
            target,
            ActivityDirection.Outbound,
            (Domain.Visibility)Visibility.MentionedOnly,
            "{\"type\":\"Follow\"}",
            new string('a', 64),
            false,
            Now,
            Now);
        return StreamEvent.FromRelationshipMutation(
            activity,
            relationship,
            targetId,
            recipient,
            isLocal: true);
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

    private sealed class FixedStreamStore(StreamEventPage page) : IStreamEventStore
    {
        public Task<StreamEventPage> ReadAfterAsync(
            long afterCursor,
            int limit,
            CancellationToken cancellationToken)
        {
            _ = afterCursor;
            _ = limit;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(page);
        }
    }
}
