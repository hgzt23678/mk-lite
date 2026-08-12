namespace ActivityPub.Domain;

public sealed class DriveFolder : Entity
{
    private DriveFolder()
    {
    }

    private DriveFolder(Guid id, string ownerActorIri, string name, Guid? parentId, DateTimeOffset now)
        : base(id)
    {
        OwnerActorIri = CanonicalIri.RequireAbsoluteHttp(ownerActorIri, nameof(ownerActorIri));
        Name = DomainText.Required(name, nameof(name), 256);
        if (parentId == Guid.Empty)
        {
            throw new DomainException("A drive folder parent identifier cannot be empty.");
        }

        ParentId = parentId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string OwnerActorIri { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static DriveFolder Create(string ownerActorIri, string name, Guid? parentId, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerActorIri, name, parentId, now);

    public void Update(string? name, Guid? parentId, DateTimeOffset now)
    {
        if (name is not null)
        {
            Name = DomainText.Required(name, nameof(name), 256);
        }

        if (parentId is not null)
        {
            if (parentId == Guid.Empty)
            {
                throw new DomainException("A drive folder parent identifier cannot be empty.");
            }

            ParentId = parentId;
        }

        UpdatedAt = now;
    }
}
