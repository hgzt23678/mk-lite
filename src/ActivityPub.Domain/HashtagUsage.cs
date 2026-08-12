namespace ActivityPub.Domain;

public sealed class HashtagUsage : Entity
{
    private HashtagUsage()
    {
    }

    private HashtagUsage(Guid id, string name, string ownerIri, DateTimeOffset usedAt)
        : base(id)
    {
        Name = DomainText.Required(name, nameof(name), 128);
        OwnerIri = DomainText.RequiredIri(ownerIri, nameof(ownerIri));
        UsedAt = usedAt;
    }

    public string Name { get; private set; } = string.Empty;

    public string OwnerIri { get; private set; } = string.Empty;

    public DateTimeOffset UsedAt { get; private set; }

    public static HashtagUsage Create(string name, string ownerIri, DateTimeOffset usedAt) =>
        new(Guid.NewGuid(), name, ownerIri, usedAt);
}
