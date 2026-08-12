using System.Globalization;
using System.Net;

namespace ActivityPub.Application;

public sealed class DeliveryPolicy
{
    private readonly WorkerOptions _options;

    public DeliveryPolicy(WorkerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public DeliveryDisposition Classify(
        DeliveryTransportResult result,
        int attemptNumber,
        DateTimeOffset now,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(random);

        if (result.StatusCode is >= 200 and <= 299)
        {
            return new(DeliveryFailureClass.Success, null, "delivered", "The remote inbox accepted the activity.");
        }

        if (result.StatusCode is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone)
        {
            return new(DeliveryFailureClass.EndpointGone, null, "endpoint_gone", "The remote endpoint is missing or gone.");
        }

        if (result.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            return new(DeliveryFailureClass.AuthenticationRecheck, null, "authentication_recheck", "Remote authentication state requires one re-evaluation.");
        }

        bool retryable = result.StatusCode is null or 408 or 425 or 429 or >= 500;
        if (retryable && attemptNumber < _options.MaximumDeliveryAttempts)
        {
            DateTimeOffset retryAt = result.RetryAfter is { } retryAfter && retryAfter > now
                ? retryAfter <= now.Add(_options.MaximumRetryDelay) ? retryAfter : now.Add(_options.MaximumRetryDelay)
                : NextRetryAt(attemptNumber, now, random);
            return new(DeliveryFailureClass.Retryable, retryAt, result.ErrorCode ?? "remote_transient", SafeMessage(result));
        }

        string attempts = attemptNumber.ToString(CultureInfo.InvariantCulture);
        string code = retryable ? "attempts_exhausted" : result.ErrorCode ?? "remote_permanent";
        return new(DeliveryFailureClass.Permanent, null, code, $"Delivery failed after {attempts} attempt(s): {SafeMessage(result)}");
    }

    public DateTimeOffset NextRetryAt(int attemptNumber, DateTimeOffset now, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return now.Add(ComputeFullJitterDelay(attemptNumber, random));
    }

    private TimeSpan ComputeFullJitterDelay(int attemptNumber, Random random)
    {
        double exponent = Math.Pow(2, Math.Clamp(attemptNumber - 1, 0, 30));
        double ceilingMs = Math.Min(
            _options.MaximumRetryDelay.TotalMilliseconds,
            _options.InitialRetryDelay.TotalMilliseconds * exponent);
        return TimeSpan.FromMilliseconds(Math.Max(1_000, random.NextDouble() * ceilingMs));
    }

    private static string SafeMessage(DeliveryTransportResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result.StatusCode is { } status ? $"HTTP {status.ToString(CultureInfo.InvariantCulture)}" : "Network failure"
            : result.ErrorMessage.Length <= 1_024 ? result.ErrorMessage : result.ErrorMessage[..1_024];
}
