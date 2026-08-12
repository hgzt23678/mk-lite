using System.Runtime.CompilerServices;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Streaming;

public interface ITimelineSubscriptionService
{
    Task<long> GetLatestCursorAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<TimelineMutation> SubscribeAsync(
        TimelineKind kind,
        long afterCursor,
        CancellationToken cancellationToken);
}

public sealed class TimelineSubscriptionService(
    IDurableStreamEventPump pump,
    IStreamEventStore store,
    IClientApiQueryService query,
    IAuthenticatedActorContext actorContext,
    ITimelinePresentationService presentation,
    IMisskeyStreamConnectionStatus connectionStatus,
    StreamingOptions options) : ITimelineSubscriptionService
{
    public async Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        StreamEventPage page = await store.ReadAfterAsync(0, 1, cancellationToken).ConfigureAwait(false);
        return page.LatestCursor ?? 0;
    }

    public async IAsyncEnumerable<TimelineMutation> SubscribeAsync(
        TimelineKind kind,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        StreamEventPage cursorPage = await store.ReadAfterAsync(afterCursor, 1, cancellationToken).ConfigureAwait(false);
        if (cursorPage.RequestedCursorExpired)
        {
            throw new TimelineCursorException("STREAM_CURSOR_EXPIRED");
        }

        AuthenticatedActor? actor = kind is TimelineKind.Home or TimelineKind.Hybrid
            ? await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false)
            : await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        IAsyncEnumerable<StreamEvent> events = pump.SubscribeAsync(
            afterCursor,
            options.BufferCapacity,
            options.PollInterval,
            cancellationToken);
        IAsyncEnumerator<StreamEvent> enumerator = events.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                StreamEvent item;
                try
                {
                    bool moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    connectionStatus.ReportConnected();
                    if (!moved)
                    {
                        yield break;
                    }

                    item = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (StreamCursorExpiredException)
                {
                    connectionStatus.ReportDisconnected();
                    throw new TimelineCursorException("STREAM_CURSOR_EXPIRED");
                }
                catch (StreamSlowConsumerException)
                {
                    connectionStatus.ReportDisconnected();
                    throw new TimelineCursorException("STREAM_SLOW_CONSUMER");
                }
                catch
                {
                    connectionStatus.ReportDisconnected();
                    throw;
                }

                if (item.ResourceId is not { } resourceId || item.Kind is not (
                        StreamEventKind.PostCreated or
                        StreamEventKind.PostUpdated or
                        StreamEventKind.PostDeleted or
                        StreamEventKind.ReactionChanged or
                        StreamEventKind.PollVoted))
                {
                    yield return new(item.Cursor, TimelineMutationKind.Checkpoint, string.Empty, null);
                    continue;
                }

                if (!await CanReceiveAsync(item, kind, actor?.ActorIri, cancellationToken).ConfigureAwait(false))
                {
                    yield return new(item.Cursor, TimelineMutationKind.Checkpoint, string.Empty, null);
                    continue;
                }

                string noteId = await presentation.MapNoteIdAsync(
                    resourceId,
                    item.OccurredAt,
                    cancellationToken).ConfigureAwait(false);
                if (item.Kind == StreamEventKind.PostDeleted)
                {
                    yield return new(item.Cursor, TimelineMutationKind.Remove, noteId, null);
                    continue;
                }

                NoteViewModel? note = await presentation.FindForStreamAsync(
                    resourceId,
                    kind,
                    cancellationToken).ConfigureAwait(false);
                yield return note is null
                    ? new(item.Cursor, TimelineMutationKind.Checkpoint, string.Empty, null)
                    : new(item.Cursor, TimelineMutationKind.Upsert, note.Id, note);
            }
        }
    }

    private async Task<bool> CanReceiveAsync(
        StreamEvent item,
        TimelineKind kind,
        string? actorIri,
        CancellationToken cancellationToken)
    {
        if (kind == TimelineKind.Hybrid)
        {
            return await query.CanReceiveStreamEventAsync(
                       item,
                       actorIri,
                       ClientStreamAudience.Home,
                       localOnly: false,
                       cancellationToken).ConfigureAwait(false) ||
                   await query.CanReceiveStreamEventAsync(
                       item,
                       actorIri,
                       ClientStreamAudience.Public,
                       localOnly: true,
                       cancellationToken).ConfigureAwait(false);
        }

        return await query.CanReceiveStreamEventAsync(
            item,
            actorIri,
            kind is TimelineKind.Local or TimelineKind.Global
                ? ClientStreamAudience.Public
                : ClientStreamAudience.Home,
            localOnly: kind == TimelineKind.Local,
            cancellationToken).ConfigureAwait(false);
    }
}
