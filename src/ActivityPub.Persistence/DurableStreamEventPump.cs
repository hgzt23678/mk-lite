using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Persistence;

public sealed class DurableStreamEventPump(
    IStreamEventStore store,
    IStreamEventNotifier notifier) : IDurableStreamEventPump
{
    public async IAsyncEnumerable<StreamEvent> SubscribeAsync(
        long afterCursor,
        int bufferCapacity,
        TimeSpan pollInterval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);

        Channel<StreamEvent> channel = Channel.CreateBounded<StreamEvent>(new BoundedChannelOptions(bufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task producer = ProduceAsync(
            channel.Writer,
            afterCursor,
            pollInterval,
            producerCancellation.Token);
        try
        {
            await foreach (StreamEvent item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            producerCancellation.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (producerCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<StreamEvent> writer,
        long afterCursor,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        long cursor = afterCursor;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StreamEventPage page = await store.ReadAfterAsync(cursor, 100, cancellationToken).ConfigureAwait(false);
                if (page.RequestedCursorExpired)
                {
                    throw new StreamCursorExpiredException(cursor, page.OldestAvailableCursor);
                }

                if (page.Events.Count == 0)
                {
                    await notifier.WaitAsync(pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (StreamEvent item in page.Events)
                {
                    if (!writer.TryWrite(item))
                    {
                        throw new StreamSlowConsumerException();
                    }

                    cursor = item.Cursor;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete();
            return;
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            return;
        }

        writer.TryComplete();
    }
}
