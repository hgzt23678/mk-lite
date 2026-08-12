namespace ActivityPub.Domain;

public enum MisskeyAuthSessionState
{
    Pending,
    Approved,
    Denied,
    Consumed,
    Expired
}

public sealed class MisskeyAuthSession : Entity
{
    private MisskeyAuthSession()
    {
    }

    private MisskeyAuthSession(
        Guid id,
        string sessionKey,
        string clientName,
        string? clientIconUri,
        string? clientUri,
        string? callbackUri,
        string permissions,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        if (expiresAt <= createdAt)
        {
            throw new DomainException("A MiAuth session must expire after it is created.");
        }

        SessionKey = DomainText.Required(sessionKey, nameof(sessionKey), 64);
        ClientName = DomainText.Required(clientName, nameof(clientName), 200);
        ClientIconUri = DomainText.Optional(clientIconUri, nameof(clientIconUri), 2_048);
        ClientUri = DomainText.Optional(clientUri, nameof(clientUri), 2_048);
        CallbackUri = DomainText.Optional(callbackUri, nameof(callbackUri), 2_048);
        Permissions = DomainText.Optional(permissions, nameof(permissions), 2_000) ?? string.Empty;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        State = MisskeyAuthSessionState.Pending;
    }

    public string SessionKey { get; private set; } = string.Empty;
    public string ClientName { get; private set; } = string.Empty;
    public string? ClientIconUri { get; private set; }
    public string? ClientUri { get; private set; }
    public string? CallbackUri { get; private set; }
    public string Permissions { get; private set; } = string.Empty;
    public MisskeyAuthSessionState State { get; private set; }
    public string? ActorIri { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public Guid? IssuedTokenId { get; private set; }
    public string? EncryptedToken { get; private set; }

    public static MisskeyAuthSession Create(
        string sessionKey,
        string clientName,
        string? clientIconUri,
        string? clientUri,
        string? callbackUri,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            sessionKey,
            clientName,
            clientIconUri,
            clientUri,
            callbackUri,
            SerializePermissions(permissions),
            createdAt,
            expiresAt);

    public IReadOnlyList<string> GetPermissions() =>
        Permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public void Approve(string actorIri, DateTimeOffset approvedAt)
    {
        EnsurePending(approvedAt);
        ActorIri = DomainText.RequiredIri(actorIri, nameof(actorIri));
        State = MisskeyAuthSessionState.Approved;
        DecidedAt = approvedAt;
    }

    public void AttachIssuedToken(Guid tokenId, string encryptedToken)
    {
        if (State != MisskeyAuthSessionState.Approved || IssuedTokenId is not null || tokenId == Guid.Empty)
        {
            throw new DomainException("A token can only be attached once to an approved MiAuth session.");
        }

        IssuedTokenId = tokenId;
        EncryptedToken = DomainText.Required(encryptedToken, nameof(encryptedToken), 4_096);
    }

    public void Deny(DateTimeOffset deniedAt)
    {
        EnsurePending(deniedAt);
        State = MisskeyAuthSessionState.Denied;
        DecidedAt = deniedAt;
    }

    public string Consume(DateTimeOffset consumedAt)
    {
        if (State != MisskeyAuthSessionState.Approved || ActorIri is null || IssuedTokenId is null ||
            EncryptedToken is null || consumedAt > ExpiresAt)
        {
            throw new DomainException("Only a non-expired approved MiAuth session can be consumed.");
        }

        string encryptedToken = EncryptedToken;
        State = MisskeyAuthSessionState.Consumed;
        ConsumedAt = consumedAt;
        EncryptedToken = null;
        return encryptedToken;
    }

    public void Expire(DateTimeOffset now)
    {
        if (State is (MisskeyAuthSessionState.Pending or MisskeyAuthSessionState.Approved) && now >= ExpiresAt)
        {
            State = MisskeyAuthSessionState.Expired;
        }
    }

    private void EnsurePending(DateTimeOffset now)
    {
        if (State != MisskeyAuthSessionState.Pending || now > ExpiresAt)
        {
            throw new DomainException("The MiAuth session is no longer pending.");
        }
    }

    private static string SerializePermissions(IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        string result = string.Join(' ', permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
        return result;
    }
}

public sealed class MisskeyAccessToken : Entity
{
    private MisskeyAccessToken()
    {
    }

    private MisskeyAccessToken(
        Guid id,
        string actorIri,
        string name,
        string? description,
        string? iconUri,
        string tokenHash,
        string permissions,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Guid sourceSessionId)
        : base(id)
    {
        if (expiresAt <= createdAt || sourceSessionId == Guid.Empty)
        {
            throw new DomainException("Misskey token lifetime or source session is invalid.");
        }

        ActorIri = DomainText.RequiredIri(actorIri, nameof(actorIri));
        Name = DomainText.Required(name, nameof(name), 200);
        Description = DomainText.Optional(description, nameof(description), 2_000);
        IconUri = DomainText.Optional(iconUri, nameof(iconUri), 2_048);
        TokenHash = DomainText.Required(tokenHash, nameof(tokenHash), 64);
        Permissions = DomainText.Optional(permissions, nameof(permissions), 2_000) ?? string.Empty;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        LastUsedAt = createdAt;
        SourceSessionId = sourceSessionId;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? IconUri { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string Permissions { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid SourceSessionId { get; private set; }

    public static MisskeyAccessToken Create(
        string actorIri,
        string name,
        string? description,
        string? iconUri,
        string tokenHash,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Guid sourceSessionId) =>
        new(
            Guid.NewGuid(),
            actorIri,
            name,
            description,
            iconUri,
            tokenHash,
            string.Join(' ', permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            createdAt,
            expiresAt,
            sourceSessionId);

    public IReadOnlyList<string> GetPermissions() =>
        Permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset usedAt)
    {
        if (!IsActive(usedAt))
        {
            throw new DomainException("A revoked or expired Misskey token cannot be used.");
        }

        LastUsedAt = usedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt is null)
        {
            RevokedAt = revokedAt;
        }
    }
}
