using ActivityPub.Application;

namespace ActivityPub.Federation.Tests;

public sealed class DeliveryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(200, DeliveryFailureClass.Success)]
    [InlineData(202, DeliveryFailureClass.Success)]
    [InlineData(404, DeliveryFailureClass.EndpointGone)]
    [InlineData(410, DeliveryFailureClass.EndpointGone)]
    [InlineData(401, DeliveryFailureClass.AuthenticationRecheck)]
    [InlineData(403, DeliveryFailureClass.AuthenticationRecheck)]
    [InlineData(400, DeliveryFailureClass.Permanent)]
    [InlineData(422, DeliveryFailureClass.Permanent)]
    [InlineData(429, DeliveryFailureClass.Retryable)]
    [InlineData(500, DeliveryFailureClass.Retryable)]
    public void ClassifiesHttpStatus(int status, DeliveryFailureClass expected)
    {
        DeliveryDisposition result = Policy().Classify(
            new DeliveryTransportResult(status, TimeSpan.Zero, null, null, null),
            1,
            Now,
            new Random(42));

        Assert.Equal(expected, result.Classification);
    }

    [Fact]
    public void HonorsRetryAfterWithinConfiguredLimit()
    {
        DateTimeOffset retryAfter = Now.AddMinutes(10);

        DeliveryDisposition result = Policy().Classify(
            new DeliveryTransportResult(429, TimeSpan.Zero, retryAfter, null, null),
            1,
            Now,
            new Random(42));

        Assert.Equal(retryAfter, result.RetryAt);
    }

    [Fact]
    public void CapsHostileRetryAfterAtMaximumDelay()
    {
        DeliveryDisposition result = Policy().Classify(
            new DeliveryTransportResult(429, TimeSpan.Zero, Now.AddYears(10), null, null),
            1,
            Now,
            new Random(42));

        Assert.Equal(Now.AddHours(24), result.RetryAt);
    }

    [Fact]
    public void ExhaustedTransientFailureBecomesPermanent()
    {
        DeliveryDisposition result = Policy().Classify(
            new DeliveryTransportResult(null, TimeSpan.Zero, null, "timeout", "timed out"),
            12,
            Now,
            new Random(42));

        Assert.Equal(DeliveryFailureClass.Permanent, result.Classification);
        Assert.Equal("attempts_exhausted", result.Code);
    }

    private static DeliveryPolicy Policy() => new(new WorkerOptions
    {
        MaximumDeliveryAttempts = 12,
        InitialRetryDelay = TimeSpan.FromSeconds(30),
        MaximumRetryDelay = TimeSpan.FromHours(24)
    });
}
