namespace ActivityPub.Domain;

public sealed class UserBlock : Entity
{
    private UserBlock()
    {
    }

    private UserBlock(
        Guid id,
        string ownerActorIri,
        string targetActorIri,
        string blockActivityIri,
        DateTimeOffset now)
        : base(id)
    {
        OwnerActorIri = CanonicalIri.RequireAbsoluteHttp(ownerActorIri, nameof(ownerActorIri));
        TargetActorIri = CanonicalIri.RequireAbsoluteHttp(targetActorIri, nameof(targetActorIri));
        BlockActivityIri = CanonicalIri.RequireAbsoluteHttp(blockActivityIri, nameof(blockActivityIri));
        if (string.Equals(OwnerActorIri, TargetActorIri, StringComparison.Ordinal))
        {
            throw new DomainException("An actor cannot block itself.");
        }

        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string OwnerActorIri { get; private set; } = string.Empty;
    public string TargetActorIri { get; private set; } = string.Empty;
    public string BlockActivityIri { get; private set; } = string.Empty;
    public string? UndoActivityIri { get; private set; }
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserBlock Create(
        string ownerActorIri,
        string targetActorIri,
        string blockActivityIri,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerActorIri, targetActorIri, blockActivityIri, now);

    public void Undo(string actorIri, string undoActivityIri, DateTimeOffset now)
    {
        string actor = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        if (!string.Equals(actor, OwnerActorIri, StringComparison.Ordinal))
        {
            throw new DomainException("Only the blocking actor can undo a Block activity.");
        }

        UndoActivityIri = CanonicalIri.RequireAbsoluteHttp(undoActivityIri, nameof(undoActivityIri));
        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }
}
