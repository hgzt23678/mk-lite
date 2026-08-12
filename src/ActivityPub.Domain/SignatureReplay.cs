namespace ActivityPub.Domain;

public sealed class SignatureReplay : Entity
{
    private SignatureReplay()
    {
    }

    private SignatureReplay(
        Guid id,
        string fingerprint,
        string? nonceHash,
        string keyIri,
        string activityIri,
        DateTimeOffset receivedAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Fingerprint = DomainText.Required(fingerprint, nameof(fingerprint), 128);
        NonceHash = DomainText.Optional(nonceHash, nameof(nonceHash), 128);
        KeyIri = CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ReceivedAt = receivedAt;
        ExpiresAt = expiresAt > receivedAt
            ? expiresAt
            : throw new DomainException("Signature replay record must expire after receipt.");
    }

    public string Fingerprint { get; private set; } = string.Empty;
    public string? NonceHash { get; private set; }
    public string KeyIri { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static SignatureReplay Create(
        string fingerprint,
        string? nonceHash,
        string keyIri,
        string activityIri,
        DateTimeOffset receivedAt,
        DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), fingerprint, nonceHash, keyIri, activityIri, receivedAt, expiresAt);
}
