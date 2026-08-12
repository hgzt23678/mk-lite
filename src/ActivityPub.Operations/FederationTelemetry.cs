using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ActivityPub.Operations;

public static class FederationTelemetry
{
    private static long queueDepth;
    private static long oldestPendingMilliseconds;

    public const string SourceName = "ActivityPub.Server";
    public static ActivitySource ActivitySource { get; } = new(SourceName);
    public static Meter Meter { get; } = new(SourceName);

    public static Counter<long> InboxAccepted { get; } = Meter.CreateCounter<long>("activitypub.inbox.accepted");
    public static Counter<long> ActivitiesProcessed { get; } = Meter.CreateCounter<long>("activitypub.activities.processed");
    public static Counter<long> SignatureVerified { get; } = Meter.CreateCounter<long>("activitypub.signature.verified");
    public static Counter<long> SignatureFailed { get; } = Meter.CreateCounter<long>("activitypub.signature.failed");
    public static Counter<long> DuplicateActivities { get; } = Meter.CreateCounter<long>("activitypub.inbox.duplicates");
    public static Histogram<double> InboxProcessingDelay { get; } = Meter.CreateHistogram<double>("activitypub.inbox.processing_delay", "s");
    public static Counter<long> DeliveriesSucceeded { get; } = Meter.CreateCounter<long>("activitypub.delivery.succeeded");
    public static Counter<long> DeliveryRetries { get; } = Meter.CreateCounter<long>("activitypub.delivery.retries");
    public static Counter<long> DeadLetters { get; } = Meter.CreateCounter<long>("activitypub.dead_letters");
    public static Histogram<double> RemoteLatency { get; } = Meter.CreateHistogram<double>("activitypub.remote.latency", "ms");
    public static Counter<long> RemoteStatus { get; } = Meter.CreateCounter<long>("activitypub.remote.status");
    public static Counter<long> PublicKeyCacheHits { get; } = Meter.CreateCounter<long>("activitypub.keys.cache_hits");
    public static Counter<long> PublicKeyCacheMisses { get; } = Meter.CreateCounter<long>("activitypub.keys.cache_misses");
    public static Counter<long> SsrfRejected { get; } = Meter.CreateCounter<long>("activitypub.ssrf.rejected");
    public static Counter<long> RateLimited { get; } = Meter.CreateCounter<long>("activitypub.rate_limited");
    public static UpDownCounter<long> ActiveWorkerLeases { get; } = Meter.CreateUpDownCounter<long>("activitypub.worker.active_leases");
    public static ObservableGauge<long> DeliveryQueueDepth { get; } = Meter.CreateObservableGauge(
        "activitypub.delivery.queue_depth",
        () => Interlocked.Read(ref queueDepth));
    public static ObservableGauge<double> OldestPendingDelivery { get; } = Meter.CreateObservableGauge(
        "activitypub.delivery.oldest_pending",
        () => Interlocked.Read(ref oldestPendingMilliseconds) / 1_000d,
        "s");

    public static void SetDeliveryQueue(long count, TimeSpan? oldest)
    {
        Interlocked.Exchange(ref queueDepth, count);
        Interlocked.Exchange(ref oldestPendingMilliseconds, oldest is null ? 0 : checked((long)oldest.Value.TotalMilliseconds));
    }
}

public sealed class FederationInstrumentation : ActivityPub.Application.IFederationInstrumentation
{
    public void InboxAccepted(ActivityPub.Application.InboxAcceptanceStatus status)
    {
        if (status == ActivityPub.Application.InboxAcceptanceStatus.Accepted)
        {
            FederationTelemetry.InboxAccepted.Add(1);
        }
        else if (status == ActivityPub.Application.InboxAcceptanceStatus.Duplicate)
        {
            FederationTelemetry.DuplicateActivities.Add(1);
        }
    }

    public void SignatureVerified(ActivityPub.Domain.SignatureProfile profile) =>
        FederationTelemetry.SignatureVerified.Add(1, new KeyValuePair<string, object?>("profile", profile.ToString()));

    public void SignatureFailed(string profile) =>
        FederationTelemetry.SignatureFailed.Add(1, new KeyValuePair<string, object?>("profile", profile));

    public void ActivityProcessed(string activityType, TimeSpan delay)
    {
        FederationTelemetry.ActivitiesProcessed.Add(1, new KeyValuePair<string, object?>("activity.type", activityType));
        FederationTelemetry.InboxProcessingDelay.Record(delay.TotalSeconds);
    }

    public void RemoteRequest(string domain, int statusCode, TimeSpan duration)
    {
        FederationTelemetry.RemoteLatency.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("server.domain", domain));
        FederationTelemetry.RemoteStatus.Add(1,
            new KeyValuePair<string, object?>("server.domain", domain),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode));
    }

    public void PublicKeyCache(bool hit)
    {
        (hit ? FederationTelemetry.PublicKeyCacheHits : FederationTelemetry.PublicKeyCacheMisses).Add(1);
    }

    public void SsrfRejected() => FederationTelemetry.SsrfRejected.Add(1);

    public void DeliveryCompleted(string domain, int? statusCode, ActivityPub.Domain.DeliveryAttemptOutcome outcome)
    {
        KeyValuePair<string, object?> domainTag = new("server.domain", domain);
        KeyValuePair<string, object?> statusTag = new("http.response.status_code", statusCode);
        if (outcome == ActivityPub.Domain.DeliveryAttemptOutcome.Succeeded)
        {
            FederationTelemetry.DeliveriesSucceeded.Add(1, domainTag, statusTag);
        }
        else if (outcome == ActivityPub.Domain.DeliveryAttemptOutcome.RetryScheduled)
        {
            FederationTelemetry.DeliveryRetries.Add(1, domainTag, statusTag);
        }
        else if (outcome == ActivityPub.Domain.DeliveryAttemptOutcome.TerminalFailure)
        {
            FederationTelemetry.DeadLetters.Add(1, domainTag, statusTag);
        }
    }

    public void LeaseDelta(string workerType, int delta) =>
        FederationTelemetry.ActiveWorkerLeases.Add(delta, new KeyValuePair<string, object?>("worker.type", workerType));
}
