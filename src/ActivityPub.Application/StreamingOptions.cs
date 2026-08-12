namespace ActivityPub.Application;

public sealed class StreamingOptions
{
    public int BufferCapacity { get; init; } = 256;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);
    public int MaximumInboundMessageBytes { get; init; } = 64 * 1024;
    public int MaximumConnectionsPerUser { get; init; } = 5;
    public int MaximumConnectionsPerIp { get; init; } = 25;
    public TimeSpan ConnectionLeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ConnectionLeaseRenewalInterval { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (BufferCapacity is < 8 or > 10_000)
        {
            throw new InvalidOperationException("Streaming:BufferCapacity must be between 8 and 10000.");
        }

        if (PollInterval < TimeSpan.FromMilliseconds(25) || PollInterval > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("Streaming:PollInterval must be between 25 milliseconds and 30 seconds.");
        }

        if (HeartbeatInterval < TimeSpan.FromSeconds(1) || HeartbeatInterval > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException("Streaming:HeartbeatInterval must be between 1 second and 2 minutes.");
        }

        if (MaximumInboundMessageBytes is < 1024 or > 1024 * 1024)
        {
            throw new InvalidOperationException("Streaming:MaximumInboundMessageBytes must be between 1024 and 1048576.");
        }


        if (MaximumConnectionsPerUser is < 1 or > 100 || MaximumConnectionsPerIp is < 1 or > 1000)
        {
            throw new InvalidOperationException("Streaming connection limits are outside the supported range.");
        }

        if (ConnectionLeaseDuration < TimeSpan.FromSeconds(10) ||
            ConnectionLeaseRenewalInterval < TimeSpan.FromSeconds(1) ||
            ConnectionLeaseRenewalInterval >= ConnectionLeaseDuration)
        {
            throw new InvalidOperationException("Streaming connection lease renewal must be positive and shorter than its duration.");
        }
    }
}
