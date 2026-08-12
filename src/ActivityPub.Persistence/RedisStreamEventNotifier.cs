using System.Text.Json;
using ActivityPub.Application;
using StackExchange.Redis;

namespace ActivityPub.Persistence;

public sealed class RedisStreamEventNotifier(
    string? connectionString,
    string? channelName = null) : IStreamEventNotifier, IDisposable
{
    private const string DefaultChannel = "activitypub:stream-events";
    private readonly string channel = string.IsNullOrWhiteSpace(channelName) ? DefaultChannel : channelName;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private ConnectionMultiplexer? connection;
    private TaskCompletionSource<long> wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(connectionString);

    public async Task PublishAsync(IReadOnlyList<long> cursors, CancellationToken cancellationToken)
    {
        if (!IsEnabled || cursors.Count == 0)
        {
            return;
        }

        try
        {
            ISubscriber publisher = await GetSubscriberAsync(cancellationToken).ConfigureAwait(false);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { cursors });
            await publisher.PublishAsync(RedisChannel.Literal(channel), payload).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _ = exception;
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
            return;
        }

        Task<long> pending = wake.Task;
        Task completed = await Task.WhenAny(
            pending,
            Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (completed != pending)
        {
            return;
        }

        _ = pending.Result;
        Interlocked.Exchange(
            ref wake,
            new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        connectionGate.Dispose();
        connection?.Dispose();
        connection = null;
        GC.SuppressFinalize(this);
    }

    private static string NormalizeConfiguration(string connectionString)
    {
        string normalized = connectionString;
        if (normalized.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["rediss://".Length..];
        }
        else if (normalized.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["redis://".Length..];
        }

        return normalized + ",abortConnect=false,connectTimeout=3000,syncTimeout=3000,asyncTimeout=3000";
    }

    private async Task<ISubscriber> GetSubscriberAsync(CancellationToken cancellationToken)
    {
        ConnectionMultiplexer? current = connection;
        if (current is not null && current.IsConnected)
        {
            return current.GetSubscriber();
        }

        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection is not null && connection.IsConnected)
            {
                return connection.GetSubscriber();
            }

            string configuration = NormalizeConfiguration(connectionString!);
            ConnectionMultiplexer created = await ConnectionMultiplexer.ConnectAsync(
                ConfigurationOptions.Parse(configuration))
                .ConfigureAwait(false);
            connection = created;
            ISubscriber newSubscriber = created.GetSubscriber();
            _ = newSubscriber.SubscribeAsync(
                RedisChannel.Literal(channel),
                (_, _) =>
                {
                    TaskCompletionSource<long> pending = Interlocked.Exchange(
                        ref wake,
                        new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
                    pending.TrySetResult(1);
                });
            return newSubscriber;
        }
        finally
        {
            connectionGate.Release();
        }
    }
}
