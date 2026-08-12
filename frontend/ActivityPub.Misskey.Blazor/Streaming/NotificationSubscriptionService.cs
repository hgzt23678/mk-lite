using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Streaming;

public enum NotificationMutationKind
{
    Checkpoint,
    Created
}

public sealed record NotificationMutation(
    long Cursor,
    NotificationMutationKind Kind,
    NotificationViewModel? Notification);

public interface INotificationSubscriptionService
{
    Task<long> GetLatestCursorAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<NotificationMutation> SubscribeAsync(
        long afterCursor,
        IReadOnlySet<MisskeyNotificationType>? includeTypes,
        IReadOnlySet<MisskeyNotificationType>? excludeTypes,
        CancellationToken cancellationToken);
}

public sealed class NotificationSubscriptionService(
    IDurableStreamEventPump pump,
    IStreamEventStore store,
    IAuthenticatedActorContext actorContext,
    INotificationPresentationService notifications,
    StreamingOptions options) : INotificationSubscriptionService
{
    public async Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        StreamEventPage page = await store.ReadAfterAsync(0, 1, cancellationToken).ConfigureAwait(false);
        return page.LatestCursor ?? 0;
    }

    public async IAsyncEnumerable<NotificationMutation> SubscribeAsync(
        long afterCursor,
        IReadOnlySet<MisskeyNotificationType>? includeTypes,
        IReadOnlySet<MisskeyNotificationType>? excludeTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        StreamEventPage cursorPage = await store.ReadAfterAsync(afterCursor, 1, cancellationToken).ConfigureAwait(false);
        if (cursorPage.RequestedCursorExpired)
        {
            throw new NotificationCursorException("STREAM_CURSOR_EXPIRED");
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
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
                    throw new NotificationCursorException("STREAM_CURSOR_EXPIRED");
                }
                catch (StreamSlowConsumerException)
                {
                    throw new NotificationCursorException("STREAM_SLOW_CONSUMER");
                }

                if (item.Kind != StreamEventKind.NotificationCreated ||
                    item.ResourceId is not { } notificationId ||
                    !string.Equals(item.RecipientActorIri, actor.ActorIri, StringComparison.Ordinal))
                {
                    yield return new(item.Cursor, NotificationMutationKind.Checkpoint, null);
                    continue;
                }

                NotificationViewModel? notification = await notifications.FindAsync(
                    notificationId,
                    cancellationToken).ConfigureAwait(false);
                if (notification is null || !Matches(notification.Type, includeTypes, excludeTypes))
                {
                    yield return new(item.Cursor, NotificationMutationKind.Checkpoint, null);
                    continue;
                }

                yield return new(item.Cursor, NotificationMutationKind.Created, notification);
            }
        }
    }

    private static bool Matches(
        MisskeyNotificationType type,
        IReadOnlySet<MisskeyNotificationType>? included,
        IReadOnlySet<MisskeyNotificationType>? excluded) =>
        (included is null || included.Count == 0 || included.Contains(type)) &&
        (excluded is null || !excluded.Contains(type));
}

public sealed class NotificationCursorException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
