using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;

namespace ActivityPub.Misskey.Blazor.Streaming;

public interface IRelationshipSubscriptionService
{
    Task<long> GetLatestCursorAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
        Guid targetActorId,
        long afterCursor,
        CancellationToken cancellationToken);
}

public sealed record RelationshipMutation(long Cursor, bool Changed);

public sealed class RelationshipSubscriptionService(
    IDurableStreamEventPump pump,
    IStreamEventStore store,
    IAuthenticatedActorContext actorContext,
    StreamingOptions options) : IRelationshipSubscriptionService
{
    public async Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        StreamEventPage page = await store.ReadAfterAsync(0, 1, cancellationToken).ConfigureAwait(false);
        return page.LatestCursor ?? 0;
    }

    public async IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
        Guid targetActorId,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (targetActorId == Guid.Empty)
        {
            throw new ArgumentException("A relationship stream target is required.", nameof(targetActorId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        StreamEventPage cursorPage = await store.ReadAfterAsync(afterCursor, 1, cancellationToken).ConfigureAwait(false);
        if (cursorPage.RequestedCursorExpired)
        {
            throw new RelationshipCursorException("STREAM_CURSOR_EXPIRED");
        }

        AuthenticatedActor viewer = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        IAsyncEnumerator<StreamEvent> enumerator = pump.SubscribeAsync(
            afterCursor,
            options.BufferCapacity,
            options.PollInterval,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                StreamEvent item;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }

                    item = enumerator.Current;
                }
                catch (StreamCursorExpiredException)
                {
                    throw new RelationshipCursorException("STREAM_CURSOR_EXPIRED");
                }
                catch (StreamSlowConsumerException)
                {
                    throw new RelationshipCursorException("STREAM_SLOW_CONSUMER");
                }

                bool changed = item.Kind == StreamEventKind.RelationshipChanged &&
                    item.ResourceId == targetActorId &&
                    string.Equals(item.RecipientActorIri, viewer.ActorIri, StringComparison.Ordinal);
                yield return new(item.Cursor, changed);
            }
        }
    }
}

public sealed class RelationshipCursorException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
