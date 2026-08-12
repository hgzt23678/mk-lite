namespace ActivityPub.Identity;

public sealed class LocalRegistrationInvitation
{
    private LocalRegistrationInvitation()
    {
    }

    public Guid Id { get; private set; }

    public byte[] CodeHash { get; private set; } = [];

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? ReservationId { get; private set; }

    public DateTimeOffset? ReservedAt { get; private set; }

    public DateTimeOffset? ReservationExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? ConsumedByUsername { get; private set; }

    public static LocalRegistrationInvitation Create(
        byte[] codeHash,
        string createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(codeHash);
        if (codeHash.Length != 32 || string.IsNullOrWhiteSpace(createdBy) || createdBy.Length > 256 ||
            expiresAt <= createdAt)
        {
            throw new ArgumentException("A bounded operator, SHA-256 code hash, and future expiry are required.");
        }

        return new LocalRegistrationInvitation
        {
            Id = Guid.NewGuid(),
            CodeHash = codeHash.ToArray(),
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }
}
