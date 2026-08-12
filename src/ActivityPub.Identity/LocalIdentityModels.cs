using Microsoft.AspNetCore.Identity;

namespace ActivityPub.Identity;

public enum LocalAccountProvisioningState
{
    Pending = 0,
    Provisioning = 1,
    Active = 2,
    Failed = 3,
    Suspended = 4
}

public sealed class LocalIdentityUser : IdentityUser<Guid>
{
    public LocalAccountProvisioningState ProvisioningState { get; private set; } = LocalAccountProvisioningState.Pending;

    public Guid? LocalActorId { get; private set; }

    public string? LocalActorIri { get; private set; }

    public int ProvisioningAttempts { get; private set; }

    public string? ProvisioningErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? LastSignedInAt { get; private set; }

    public static LocalIdentityUser Create(string username, string? email, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserName = username,
        Email = email,
        CreatedAt = now,
        UpdatedAt = now,
        ProvisioningState = LocalAccountProvisioningState.Pending,
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    public void BeginProvisioning(DateTimeOffset now)
    {
        if (ProvisioningState is LocalAccountProvisioningState.Active or LocalAccountProvisioningState.Suspended)
        {
            throw new InvalidOperationException("An active or suspended local account cannot enter provisioning.");
        }

        ProvisioningState = LocalAccountProvisioningState.Provisioning;
        ProvisioningAttempts++;
        ProvisioningErrorCode = null;
        UpdatedAt = now;
    }

    public void Activate(Guid actorId, string actorIri, DateTimeOffset now)
    {
        if (actorId == Guid.Empty || !Uri.TryCreate(actorIri, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid ActivityPub actor is required to activate the local account.", nameof(actorIri));
        }

        LocalActorId = actorId;
        LocalActorIri = uri.AbsoluteUri;
        ProvisioningState = LocalAccountProvisioningState.Active;
        ProvisioningErrorCode = null;
        ActivatedAt = now;
        UpdatedAt = now;
    }

    public void FailProvisioning(string safeErrorCode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(safeErrorCode) || safeErrorCode.Length > 64 || safeErrorCode.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded safe provisioning error code is required.", nameof(safeErrorCode));
        }

        ProvisioningState = LocalAccountProvisioningState.Failed;
        ProvisioningErrorCode = safeErrorCode;
        UpdatedAt = now;
    }

    public void RecordSignIn(DateTimeOffset now)
    {
        LastSignedInAt = now;
        UpdatedAt = now;
    }
}

public sealed class LocalIdentityRole : IdentityRole<Guid>
{
}

public sealed record LocalAccountLookup(
    Guid UserId,
    string Username,
    string? ActorIri,
    LocalAccountProvisioningState ProvisioningState,
    bool TwoFactorEnabled,
    bool HasPasskeys);

public enum LocalAccountRegistrationStatus
{
    Created = 0,
    RegistrationDisabled = 1,
    InvalidUsername = 2,
    InvalidEmail = 3,
    InvalidPassword = 4,
    UsernameUnavailable = 5,
    EmailUnavailable = 6,
    ProvisioningFailed = 7,
    InvitationInvalid = 8,
    CaptchaInvalid = 9,
    CaptchaUnavailable = 10
}

public sealed record LocalAccountRegistrationResult(
    LocalAccountRegistrationStatus Status,
    LocalIdentityUser? User,
    IReadOnlyList<string> SafeErrorCodes);

public enum LocalAccountAuthenticationStatus
{
    Succeeded = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    TwoFactorRequired = 3,
    AccountNotActive = 4,
    EmailConfirmationRequired = 5,
    InvalidSecondFactor = 6
}

public sealed record LocalAccountAuthenticationResult(
    LocalAccountAuthenticationStatus Status,
    LocalIdentityUser? User);

public enum LocalPasskeyChallengeStatus
{
    Created = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    AccountNotActive = 3,
    EmailConfirmationRequired = 4,
    PasskeyUnavailable = 5
}

public sealed record LocalPasskeyChallengeResult(
    LocalPasskeyChallengeStatus Status,
    string? RequestOptionsJson);

public enum LocalPasskeyAuthenticationStatus
{
    Succeeded = 0,
    InvalidAssertion = 1,
    LockedOut = 2,
    AccountNotActive = 3,
    EmailConfirmationRequired = 4,
    PersistenceFailed = 5
}

public sealed record LocalPasskeyAuthenticationResult(
    LocalPasskeyAuthenticationStatus Status,
    LocalIdentityUser? User);
