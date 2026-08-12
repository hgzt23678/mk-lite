namespace ActivityPub.Domain;

public enum RelayStatus
{
    Requesting,
    Accepted,
    Rejected
}

public sealed class Relay : Entity
{
    private Relay()
    {
    }

    private Relay(Guid id, string inbox, DateTimeOffset now)
        : base(id)
    {
        Inbox = CanonicalIri.RequireAbsoluteHttp(inbox, nameof(inbox));
        Status = RelayStatus.Requesting;
        CreatedAt = now;
    }

    public string Inbox { get; private set; } = string.Empty;

    public RelayStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Relay Request(string inbox, DateTimeOffset now) => new(Guid.NewGuid(), inbox, now);

    public void Accept(DateTimeOffset now)
    {
        _ = now;
        if (Status != RelayStatus.Rejected)
        {
            Status = RelayStatus.Accepted;
        }
    }

    public void Reject(DateTimeOffset now)
    {
        _ = now;
        Status = RelayStatus.Rejected;
    }
}
