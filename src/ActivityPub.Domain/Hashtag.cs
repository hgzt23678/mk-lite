namespace ActivityPub.Domain;

public sealed class Hashtag : Entity
{
    private Hashtag()
    {
    }

    private Hashtag(Guid id, string name, DateTimeOffset now)
        : base(id)
    {
        Name = DomainText.Required(name, nameof(name), 128);
        Count = 1;
        LastUsedAt = now;
    }

    public string Name { get; private set; } = string.Empty;

    public long Count { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public static Hashtag Create(string name, DateTimeOffset now) =>
        new(Guid.NewGuid(), name, now);

    public void RecordUsage(DateTimeOffset now)
    {
        Count += 1;
        LastUsedAt = now;
    }
}
