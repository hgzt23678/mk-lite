using ActivityPub.Application;
using Microsoft.AspNetCore.Identity;

namespace ActivityPub.Identity;

public sealed record LocalRegistrationProtection(
    string? InvitationCode,
    string? HcaptchaResponse,
    string? RecaptchaResponse,
    string? RemoteIpAddress = null);

public enum RegistrationProtectionStatus
{
    Accepted = 0,
    InvitationInvalid = 1,
    CaptchaInvalid = 2,
    CaptchaUnavailable = 3
}

public sealed record RegistrationProtectionResult(
    RegistrationProtectionStatus Status,
    RegistrationInvitationReservation? InvitationReservation);

public interface IRegistrationProtectionService
{
    Task<RegistrationProtectionResult> AuthorizeAsync(
        LocalRegistrationProtection protection,
        CancellationToken cancellationToken);

    Task<bool> ConsumeInvitationAsync(
        RegistrationInvitationReservation reservation,
        string username,
        CancellationToken cancellationToken);

    Task ReleaseInvitationAsync(
        RegistrationInvitationReservation reservation,
        CancellationToken cancellationToken);
}

public interface IRegistrationCaptchaVerifier
{
    Task<RegistrationCaptchaVerificationResult> VerifyAsync(
        RegistrationCaptchaProvider provider,
        string response,
        string? remoteIpAddress,
        CancellationToken cancellationToken);
}

public enum RegistrationCaptchaVerificationResult
{
    Valid = 0,
    Invalid = 1,
    Unavailable = 2
}

public sealed record AtomicLocalAccountCreationResult(
    bool InvitationAccepted,
    bool InvitationConsumed,
    IdentityResult IdentityResult);

public interface IAtomicLocalAccountRegistration
{
    Task<AtomicLocalAccountCreationResult> CreateAsync(
        LocalIdentityUser user,
        string password,
        RegistrationInvitationReservation? invitationReservation,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);
}
