using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActivityPub.Persistence;

/// <summary>
/// Provides lossy Redis acceleration for PostgreSQL-backed projections and a
/// delivery-worker wake-up signal. Redis never owns a delivery job, timeline
/// object, notification, cursor, or authorization decision.
/// </summary>
public sealed class RedisAccelerationService :
    IFederationQueueSignal,
    IClientProjectionCache,
    IDisposable
{
    private static readonly Meter Meter = new("ActivityPub.Server");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("activitypub.redis.cache_hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("activitypub.redis.cache_misses");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("activitypub.redis.failures");
    private static readonly Counter<long> Wakeups = Meter.CreateCounter<long>("activitypub.redis.wakeups");
    private static readonly Action<ILogger, string, Exception?> RedisUnavailable = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2_101, nameof(RedisUnavailable)),
        "Redis acceleration is unavailable ({FailureType}); PostgreSQL polling and queries remain active");

    private readonly string? connectionString;
    private readonly string keyPrefix;
    private readonly string deliveryChannel;
    private readonly string inboxChannel;
    private readonly TimeSpan timelineTtl;
    private readonly TimeSpan notificationTtl;
    private readonly ILogger<RedisAccelerationService> logger;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private ConnectionMultiplexer? connection;
    private TaskCompletionSource<bool> deliveryWake = NewWakeSource();
    private TaskCompletionSource<bool> inboxWake = NewWakeSource();
    private DateTimeOffset reconnectAfter;
    private bool disposed;

    public RedisAccelerationService(
        string? connectionString,
        string? keyPrefix,
        string? deliveryChannel,
        string? inboxChannel,
        TimeSpan timelineTtl,
        TimeSpan notificationTtl,
        ILogger<RedisAccelerationService> logger)
    {
        if (timelineTtl < TimeSpan.FromMilliseconds(100) || timelineTtl > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timelineTtl));
        }

        if (notificationTtl < TimeSpan.FromMilliseconds(100) || notificationTtl > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(notificationTtl));
        }

        this.connectionString = connectionString;
        this.keyPrefix = NormalizeToken(keyPrefix, "activitypub");
        this.deliveryChannel = NormalizeToken(deliveryChannel, $"{this.keyPrefix}:delivery-wakeup");
        this.inboxChannel = NormalizeToken(inboxChannel, $"{this.keyPrefix}:inbox-wakeup");
        this.timelineTtl = timelineTtl;
        this.notificationTtl = notificationTtl;
        this.logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(connectionString);

    public async Task NotifyDeliveryAvailableAsync(CancellationToken cancellationToken)
        => await PublishWakeupAsync(deliveryChannel, cancellationToken).ConfigureAwait(false);

    public async Task NotifyInboxAvailableAsync(CancellationToken cancellationToken)
        => await PublishWakeupAsync(inboxChannel, cancellationToken).ConfigureAwait(false);

    private async Task PublishWakeupAsync(string channel, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            ConnectionMultiplexer? current = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                await current.GetSubscriber()
                    .PublishAsync(RedisChannel.Literal(channel), RedisValue.EmptyString)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                Wakeups.Add(1, new KeyValuePair<string, object?>("queue", channel == deliveryChannel ? "delivery" : "inbox"));
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure(exception);
        }
    }

    public async Task WaitForDeliveryAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitForWakeupAsync(
            () => deliveryWake.Task,
            timeout,
            cancellationToken).ConfigureAwait(false);

    public async Task WaitForInboxAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitForWakeupAsync(
            () => inboxWake.Task,
            timeout,
            cancellationToken).ConfigureAwait(false);

    private async Task WaitForWakeupAsync(
        Func<Task> wakeTask,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (!IsEnabled)
        {
            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Capture before connection work so a wake-up cannot be lost between
        // subscribing and observing the current generation. Pub/Sub itself is
        // still lossy by design; the timeout always falls back to PostgreSQL.
        Task pending = wakeTask();
        try
        {
            _ = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure(exception);
        }

        Task delay = Task.Delay(timeout, cancellationToken);
        Task completed = await Task.WhenAny(pending, delay).ConfigureAwait(false);
        if (completed == pending)
        {
            await pending.ConfigureAwait(false);
            return;
        }

        await delay.ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>?> GetTimelineCandidatesAsync(
        string timeline,
        string? viewerActorIri,
        Guid? beforeId,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        string key = BuildTimelineKey(timeline, viewerActorIri, beforeId, candidateLimit);
        RedisValue value = await TryStringGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
        {
            CacheMisses.Add(1, new KeyValuePair<string, object?>("cache", "timeline"));
            return null;
        }

        try
        {
            byte[] payload = (byte[])value!;
            if (payload.Length > 20_000)
            {
                await TryKeyDeleteAsync(key, cancellationToken).ConfigureAwait(false);
                return null;
            }

            Guid[]? ids = JsonSerializer.Deserialize<Guid[]>(payload);
            if (ids is null || ids.Length > candidateLimit)
            {
                await TryKeyDeleteAsync(key, cancellationToken).ConfigureAwait(false);
                return null;
            }

            CacheHits.Add(1, new KeyValuePair<string, object?>("cache", "timeline"));
            return ids;
        }
        catch (JsonException)
        {
            await TryKeyDeleteAsync(key, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    public Task SetTimelineCandidatesAsync(
        string timeline,
        string? viewerActorIri,
        Guid? beforeId,
        int candidateLimit,
        IReadOnlyList<Guid> objectIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        string key = BuildTimelineKey(timeline, viewerActorIri, beforeId, candidateLimit);
        byte[] value = JsonSerializer.SerializeToUtf8Bytes(objectIds);
        return TryStringSetAsync(key, value, timelineTtl, cancellationToken);
    }

    public async Task<long?> GetUnreadNotificationCountAsync(
        string recipientActorIri,
        CancellationToken cancellationToken)
    {
        RedisValue value = await TryStringGetAsync(BuildNotificationKey(recipientActorIri), cancellationToken)
            .ConfigureAwait(false);
        long? result = !value.IsNullOrEmpty &&
            long.TryParse(value.ToString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long count) && count >= 0
                ? count
                : null;
        (result is null ? CacheMisses : CacheHits).Add(
            1,
            new KeyValuePair<string, object?>("cache", "notification-count"));
        return result;
    }

    public Task SetUnreadNotificationCountAsync(
        string recipientActorIri,
        long count,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return TryStringSetAsync(
            BuildNotificationKey(recipientActorIri),
            count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            notificationTtl,
            cancellationToken);
    }

    public Task InvalidateNotificationsAsync(
        string recipientActorIri,
        CancellationToken cancellationToken) =>
        TryKeyDeleteAsync(BuildNotificationKey(recipientActorIri), cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        deliveryWake.TrySetCanceled();
        inboxWake.TrySetCanceled();
        connectionGate.Dispose();
        connection?.Dispose();
        connection = null;
        GC.SuppressFinalize(this);
    }

    private async Task<RedisValue> TryStringGetAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return RedisValue.Null;
        }

        try
        {
            ConnectionMultiplexer? current = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            return current is null
                ? RedisValue.Null
                : await current.GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure(exception);
            return RedisValue.Null;
        }
    }

    private async Task TryStringSetAsync(
        string key,
        RedisValue value,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            ConnectionMultiplexer? current = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                _ = await current.GetDatabase().StringSetAsync(key, value, ttl)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure(exception);
        }
    }

    private async Task TryKeyDeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            ConnectionMultiplexer? current = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                _ = await current.GetDatabase().KeyDeleteAsync(key)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure(exception);
        }
    }

    private async Task<ConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsEnabled)
        {
            return null;
        }

        ConnectionMultiplexer? current = connection;
        if (current is not null)
        {
            if (!current.IsConnected && DateTimeOffset.UtcNow < reconnectAfter)
            {
                return null;
            }

            return current;
        }

        if (DateTimeOffset.UtcNow < reconnectAfter)
        {
            return null;
        }

        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection is not null)
            {
                return connection;
            }

            if (DateTimeOffset.UtcNow < reconnectAfter)
            {
                return null;
            }

            ConfigurationOptions options = ConfigurationOptions.Parse(NormalizeConfiguration(connectionString!));
            ConnectionMultiplexer created = await ConnectionMultiplexer.ConnectAsync(options)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            await created.GetSubscriber().SubscribeAsync(
                RedisChannel.Literal(deliveryChannel),
                (_, _) =>
                {
                    TaskCompletionSource<bool> wake = Interlocked.Exchange(ref deliveryWake, NewWakeSource());
                    wake.TrySetResult(true);
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            await created.GetSubscriber().SubscribeAsync(
                RedisChannel.Literal(inboxChannel),
                (_, _) =>
                {
                    TaskCompletionSource<bool> wake = Interlocked.Exchange(ref inboxWake, NewWakeSource());
                    wake.TrySetResult(true);
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            connection = created;
            return created;
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private string BuildTimelineKey(string timeline, string? viewerActorIri, Guid? beforeId, int candidateLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeline);
        if (candidateLimit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateLimit));
        }

        string scope = Hash(timeline);
        string viewer = viewerActorIri is null ? "anonymous" : Hash(viewerActorIri);
        return $"{keyPrefix}:timeline:{scope}:{viewer}:{beforeId?.ToString("N") ?? "head"}:{candidateLimit}";
    }

    private string BuildNotificationKey(string recipientActorIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientActorIri);
        return $"{keyPrefix}:notification-unread:{Hash(recipientActorIri)}";
    }

    private void RecordFailure(Exception exception)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool shouldLog = now >= reconnectAfter;
        reconnectAfter = now.AddSeconds(5);
        if (shouldLog)
        {
            RedisUnavailable(logger, exception.GetType().Name, null);
        }

        Failures.Add(1, new KeyValuePair<string, object?>("failure.type", exception.GetType().Name));
    }

    private static TaskCompletionSource<bool> NewWakeSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];

    private static string NormalizeToken(string? value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (result.Length > 200 || result.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("Redis key and channel tokens must be compact printable values.", nameof(value));
        }

        return result;
    }

    private static string NormalizeConfiguration(string value)
    {
        string normalized = value;
        bool useTls = false;
        if (normalized.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["rediss://".Length..];
            useTls = true;
        }
        else if (normalized.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["redis://".Length..];
        }

        return normalized + (useTls ? ",ssl=true" : string.Empty) +
            ",abortConnect=false,connectTimeout=3000,syncTimeout=3000,asyncTimeout=3000";
    }
}
