namespace ActivityPub.Domain;

public sealed class InboxConflict : Entity
{
    private InboxConflict()
    {
    }

    private InboxConflict(
        Guid id,
        string activityIri,
        string existingPayloadHash,
        string incomingPayloadHash,
        byte[] incomingBody,
        DateTimeOffset detectedAt)
        : base(id)
    {
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ExistingPayloadHash = DomainText.Required(existingPayloadHash, nameof(existingPayloadHash), 128);
        IncomingPayloadHash = DomainText.Required(incomingPayloadHash, nameof(incomingPayloadHash), 128);
        IncomingBody = incomingBody.Length is > 0 and <= 2_000_000
            ? incomingBody.ToArray()
            : throw new DomainException("Conflicting inbox payload size is outside the accepted range.");
        DetectedAt = detectedAt;
    }

    public string ActivityIri { get; private set; } = string.Empty;
    public string ExistingPayloadHash { get; private set; } = string.Empty;
    public string IncomingPayloadHash { get; private set; } = string.Empty;
    public byte[] IncomingBody { get; private set; } = [];
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedBy { get; private set; }

    public static InboxConflict Create(
        string activityIri,
        string existingPayloadHash,
        string incomingPayloadHash,
        byte[] incomingBody,
        DateTimeOffset detectedAt) =>
        new(Guid.NewGuid(), activityIri, existingPayloadHash, incomingPayloadHash, incomingBody, detectedAt);

    public void MarkReviewed(string operatorId, DateTimeOffset reviewedAt)
    {
        if (ReviewedAt is not null)
        {
            throw new DomainException("Inbox conflict has already been reviewed.");
        }

        ReviewedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        ReviewedAt = reviewedAt;
    }
}
