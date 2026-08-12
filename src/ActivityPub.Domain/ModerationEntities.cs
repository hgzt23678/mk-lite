using System.Security.Cryptography;
using System.Text;

namespace ActivityPub.Domain;

public sealed class DomainPolicy : Entity
{
    private DomainPolicy()
    {
    }

    private DomainPolicy(Guid id, string domain, FederationPolicyKind kind, string reason, string createdBy, DateTimeOffset now, DateTimeOffset? expiresAt)
        : base(id)
    {
        Domain = NormalizeDomain(domain);
        Kind = kind;
        Reason = DomainText.Required(reason, nameof(reason), 2_000);
        CreatedBy = DomainText.Required(createdBy, nameof(createdBy), 256);
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public string Domain { get; private set; } = string.Empty;
    public FederationPolicyKind Kind { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    public bool IsEffective(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static DomainPolicy Create(string domain, FederationPolicyKind kind, string reason, string createdBy, DateTimeOffset now, DateTimeOffset? expiresAt) =>
        new(Guid.NewGuid(), domain, kind, reason, createdBy, now, expiresAt);

    public void Revoke(string operatorId, DateTimeOffset now)
    {
        RevokedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        RevokedAt = now;
    }

    private static string NormalizeDomain(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var idn = new System.Globalization.IdnMapping();
        string ascii = idn.GetAscii(value.Trim().TrimEnd('.')).ToLowerInvariant();
        if (!Uri.CheckHostName(ascii).Equals(UriHostNameType.Dns))
        {
            throw new DomainException("Domain policy target is not a valid DNS name.");
        }

        return ascii;
    }
}

public sealed class ActorPolicy : Entity
{
    private ActorPolicy()
    {
    }

    private ActorPolicy(Guid id, string actorIri, ModerationActionKind kind, string reason, string createdBy, DateTimeOffset now, DateTimeOffset? expiresAt)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        Kind = kind;
        Reason = DomainText.Required(reason, nameof(reason), 2_000);
        CreatedBy = DomainText.Required(createdBy, nameof(createdBy), 256);
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public ModerationActionKind Kind { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    public bool IsEffective(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static ActorPolicy Create(string actorIri, ModerationActionKind kind, string reason, string createdBy, DateTimeOffset now, DateTimeOffset? expiresAt) =>
        new(Guid.NewGuid(), actorIri, kind, reason, createdBy, now, expiresAt);

    public void Revoke(string operatorId, DateTimeOffset now)
    {
        RevokedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        RevokedAt = now;
    }
}

public sealed class ModerationAction : Entity
{
    private ModerationAction()
    {
    }

    private ModerationAction(Guid id, ModerationActionKind kind, string target, string reason, string operatorId, DateTimeOffset now, DateTimeOffset? expiresAt)
        : base(id)
    {
        Kind = kind;
        Target = DomainText.Required(target, nameof(target), 2_048);
        Reason = DomainText.Required(reason, nameof(reason), 2_000);
        OperatorId = DomainText.Required(operatorId, nameof(operatorId), 256);
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public ModerationActionKind Kind { get; private set; }
    public string Target { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string OperatorId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    public static ModerationAction Create(ModerationActionKind kind, string target, string reason, string operatorId, DateTimeOffset now, DateTimeOffset? expiresAt) =>
        new(Guid.NewGuid(), kind, target, reason, operatorId, now, expiresAt);

    public void Revoke(string operatorId, DateTimeOffset now)
    {
        RevokedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        RevokedAt = now;
    }
}

public sealed class Report : Entity
{
    private Report()
    {
    }

    private Report(Guid id, string? iri, string reporterIri, string targetIri, string rawJson, DateTimeOffset now)
        : base(id)
    {
        Iri = iri is null ? null : CanonicalIri.RequireAbsoluteHttp(iri, nameof(iri));
        ReporterIri = CanonicalIri.RequireAbsoluteHttp(reporterIri, nameof(reporterIri));
        TargetIri = CanonicalIri.RequireAbsoluteHttp(targetIri, nameof(targetIri));
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        CreatedAt = now;
    }

    public string? Iri { get; private set; }
    public string ReporterIri { get; private set; } = string.Empty;
    public string TargetIri { get; private set; } = string.Empty;
    public string RawJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    public static Report Create(string? iri, string reporterIri, string targetIri, string rawJson, DateTimeOffset now) =>
        new(Guid.NewGuid(), iri, reporterIri, targetIri, rawJson, now);

    public void Resolve(string operatorId, DateTimeOffset now)
    {
        ResolvedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        ResolvedAt = now;
    }
}

public sealed class AuditEvent : Entity
{
    private AuditEvent()
    {
    }

    private AuditEvent(
        Guid id,
        string category,
        string action,
        string actor,
        string target,
        string detailsJson,
        string? previousHash,
        DateTimeOffset now)
        : base(id)
    {
        Category = DomainText.Required(category, nameof(category), 128);
        Action = DomainText.Required(action, nameof(action), 128);
        Actor = DomainText.Required(actor, nameof(actor), 256);
        Target = DomainText.Required(target, nameof(target), 2_048);
        DetailsJson = DomainText.Required(detailsJson, nameof(detailsJson), 64_000);
        PreviousHash = DomainText.Optional(previousHash, nameof(previousHash), 128);
        CreatedAt = now;
        EventHash = ComputeHash(id, Category, Action, Actor, Target, DetailsJson, PreviousHash, now);
    }

    public string Category { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public string Target { get; private set; } = string.Empty;
    public string DetailsJson { get; private set; } = string.Empty;
    public string? PreviousHash { get; private set; }
    public string EventHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditEvent Create(
        string category,
        string action,
        string actor,
        string target,
        string detailsJson,
        string? previousHash,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), category, action, actor, target, detailsJson, previousHash, now);

    private static string ComputeHash(
        Guid id,
        string category,
        string action,
        string actor,
        string target,
        string details,
        string? previousHash,
        DateTimeOffset timestamp)
    {
        string canonical = string.Join('\n', id, category, action, actor, target, details, previousHash ?? string.Empty, timestamp.ToUnixTimeMilliseconds());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
