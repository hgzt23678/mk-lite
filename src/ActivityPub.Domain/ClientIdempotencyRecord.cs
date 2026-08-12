namespace ActivityPub.Domain;

public sealed class ClientIdempotencyRecord : Entity
{
    private ClientIdempotencyRecord()
    {
    }

    private ClientIdempotencyRecord(
        Guid id,
        string subject,
        string key,
        string requestHash,
        string activityIri,
        string? objectIri,
        byte[] responseBody,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Subject = DomainText.Required(subject, nameof(subject), 256);
        Key = DomainText.Required(key, nameof(key), 256);
        RequestHash = DomainText.Required(requestHash, nameof(requestHash), 128);
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ObjectIri = objectIri is null ? null : CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        ResponseBody = responseBody.Length is > 0 and <= 2_000_000
            ? responseBody.ToArray()
            : throw new DomainException("Idempotent response body size is outside the accepted range.");
        CreatedAt = now;
        ExpiresAt = expiresAt > now ? expiresAt : throw new DomainException("Idempotency expiration must be in the future.");
    }

    public string Subject { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public string? ObjectIri { get; private set; }
    public byte[] ResponseBody { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static ClientIdempotencyRecord Create(
        string subject,
        string key,
        string requestHash,
        string activityIri,
        string? objectIri,
        byte[] responseBody,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), subject, key, requestHash, activityIri, objectIri, responseBody, now, expiresAt);
}
