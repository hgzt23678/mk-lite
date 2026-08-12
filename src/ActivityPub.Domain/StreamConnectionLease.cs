namespace ActivityPub.Domain;

public sealed class StreamConnectionLease : Entity
{
    private StreamConnectionLease()
    {
    }

    private StreamConnectionLease(
        Guid id,
        string? subject,
        string remoteAddress,
        string instanceId,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Subject = DomainText.Optional(subject, nameof(subject), 2_048);
        RemoteAddress = DomainText.Required(remoteAddress, nameof(remoteAddress), 128);
        InstanceId = DomainText.Required(instanceId, nameof(instanceId), 512);
        AcquiredAt = now;
        LastHeartbeatAt = now;
        ExpiresAt = expiresAt > now
            ? expiresAt
            : throw new DomainException("A streaming connection lease must expire after it is acquired.");
    }

    public string? Subject { get; private set; }
    public string RemoteAddress { get; private set; } = string.Empty;
    public string InstanceId { get; private set; } = string.Empty;
    public DateTimeOffset AcquiredAt { get; private set; }
    public DateTimeOffset LastHeartbeatAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static StreamConnectionLease Acquire(
        string? subject,
        string remoteAddress,
        string instanceId,
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("A streaming connection lease duration must be positive.");
        }

        return new(Guid.NewGuid(), subject, remoteAddress, instanceId, now, now.Add(duration));
    }

    public void Extend(string instanceId, DateTimeOffset now, TimeSpan duration)
    {
        if (!string.Equals(InstanceId, DomainText.Required(instanceId, nameof(instanceId), 512), StringComparison.Ordinal))
        {
            throw new DomainException("A streaming connection lease can only be extended by its owner instance.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("A streaming connection lease duration must be positive.");
        }

        LastHeartbeatAt = now;
        ExpiresAt = now.Add(duration);
    }
}
