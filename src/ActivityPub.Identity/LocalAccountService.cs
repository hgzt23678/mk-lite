using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Identity;

public interface ILocalAccountService : IRegistrationAvailabilityService
{
    Task<LocalAccountLookup?> FindAsync(string username, CancellationToken cancellationToken);

    Task<LocalAccountRegistrationResult> RegisterAsync(
        string username,
        string? email,
        string password,
        CancellationToken cancellationToken);

    Task<LocalAccountRegistrationResult> RegisterAsync(
        string username,
        string? email,
        string password,
        LocalRegistrationProtection protection,
        CancellationToken cancellationToken);

    Task<LocalAccountAuthenticationResult> AuthenticatePasswordAsync(
        string username,
        string password,
        string? authenticatorCode,
        CancellationToken cancellationToken);

    Task<LocalPasskeyChallengeResult> BeginPasskeyAuthenticationAsync(
        string username,
        string password,
        CancellationToken cancellationToken);

    Task<LocalPasskeyAuthenticationResult> AuthenticatePasskeyAsync(
        string credentialJson,
        CancellationToken cancellationToken);
}

public sealed partial class LocalAccountService(
    UserManager<LocalIdentityUser> users,
    SignInManager<LocalIdentityUser> signIn,
    IFederationQueryStore actors,
    ILocalActorAdministration actorAdministration,
    IAuditLog audit,
    IClock clock,
    LocalAccountOptions options,
    RegistrationProtectionOptions protectionOptions,
    IRegistrationProtectionService registrationProtection,
    IAtomicLocalAccountRegistration atomicRegistration,
    IPasswordVerificationTimingEqualizer passwordTimingEqualizer,
    RoleManager<LocalIdentityRole> roles,
    ILogger<LocalAccountService> logger) : ILocalAccountService
{
    public async Task<LocalAccountLookup?> FindAsync(string username, CancellationToken cancellationToken)
    {
        string normalized = NormalizeLookupUsername(username);
        if (normalized.Length == 0)
        {
            return null;
        }

        LocalIdentityUser? user = await users.FindByNameAsync(normalized).ConfigureAwait(false);
        return user is null
            ? null
            : new(
                user.Id,
                user.UserName ?? normalized,
                user.LocalActorIri,
                user.ProvisioningState,
                user.TwoFactorEnabled,
                users.SupportsUserPasskey && (await users.GetPasskeysAsync(user).ConfigureAwait(false)).Count > 0);
    }

    public async Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken)
    {
        if (!IsValidUsername(username))
        {
            return false;
        }

        if (await users.FindByNameAsync(username).ConfigureAwait(false) is not null)
        {
            return false;
        }

        return await actors.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false) is null;
    }

    public Task<RegistrationEmailAvailability> CheckEmailAvailabilityAsync(
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        email = email.Trim();
        if (email.Length is 0 or > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            return Task.FromResult(new RegistrationEmailAvailability(
                false,
                RegistrationEmailAvailabilityReason.InvalidFormat));
        }

        // This public preflight contract validates only syntax. Looking up the normalized
        // email address here would turn both the Misskey endpoint and the sign-up form into
        // an account-enumeration oracle. The unique database constraint remains authoritative
        // when registration is committed.
        return Task.FromResult(new RegistrationEmailAvailability(
            true,
            RegistrationEmailAvailabilityReason.None));
    }

    public async Task<LocalAccountRegistrationResult> RegisterAsync(
        string username,
        string? email,
        string password,
        CancellationToken cancellationToken) =>
        await RegisterAsync(
            username,
            email,
            password,
            new LocalRegistrationProtection(null, null, null),
            cancellationToken).ConfigureAwait(false);

    public async Task<LocalAccountRegistrationResult> RegisterAsync(
        string username,
        string? email,
        string password,
        LocalRegistrationProtection protection,
        CancellationToken cancellationToken)
    {
        if (!protectionOptions.RegistrationAvailable(options))
        {
            return Failure(LocalAccountRegistrationStatus.RegistrationDisabled, "REGISTRATION_DISABLED");
        }

        RegistrationProtectionResult admission = await registrationProtection
            .AuthorizeAsync(protection, cancellationToken)
            .ConfigureAwait(false);
        if (admission.Status != RegistrationProtectionStatus.Accepted)
        {
            return admission.Status switch
            {
                RegistrationProtectionStatus.InvitationInvalid =>
                    Failure(LocalAccountRegistrationStatus.InvitationInvalid, "INVALID_INVITATION_CODE"),
                RegistrationProtectionStatus.CaptchaUnavailable =>
                    Failure(LocalAccountRegistrationStatus.CaptchaUnavailable, "CAPTCHA_UNAVAILABLE"),
                _ => Failure(LocalAccountRegistrationStatus.CaptchaInvalid, "INVALID_CAPTCHA")
            };
        }

        return await RegisterAcceptedAsync(
            username,
            email,
            password,
            admission,
            initialAdministrator: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalAccountRegistrationResult> CreateInitialAdministratorAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Failure(LocalAccountRegistrationStatus.RegistrationDisabled, "LOCAL_ACCOUNTS_DISABLED");
        }

        return await RegisterAcceptedAsync(
            username,
            email: null,
            password,
            new RegistrationProtectionResult(RegistrationProtectionStatus.Accepted, null),
            initialAdministrator: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<LocalAccountRegistrationResult> RegisterAcceptedAsync(
        string username,
        string? email,
        string password,
        RegistrationProtectionResult admission,
        bool initialAdministrator,
        CancellationToken cancellationToken)
    {

        bool invitationConsumed = false;
        async Task ReleaseInvitationIfNeededAsync()
        {
            if (admission.InvitationReservation is not null && !invitationConsumed)
            {
                await registrationProtection.ReleaseInvitationAsync(
                    admission.InvitationReservation,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        async Task<LocalAccountRegistrationResult> RejectAsync(
            LocalAccountRegistrationStatus status,
            string code)
        {
            await ReleaseInvitationIfNeededAsync().ConfigureAwait(false);
            return Failure(status, code);
        }

        username = username.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (!IsValidUsername(username))
        {
            return await RejectAsync(LocalAccountRegistrationStatus.InvalidUsername, "INVALID_USERNAME").ConfigureAwait(false);
        }

        if (!initialAdministrator && options.RequireConfirmedEmail && email is null)
        {
            return await RejectAsync(LocalAccountRegistrationStatus.InvalidEmail, "EMAIL_REQUIRED").ConfigureAwait(false);
        }

        if (email is not null)
        {
            if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            {
                return await RejectAsync(LocalAccountRegistrationStatus.InvalidEmail, "INVALID_EMAIL").ConfigureAwait(false);
            }

            if (await users.FindByEmailAsync(email).ConfigureAwait(false) is not null)
            {
                return await RejectAsync(LocalAccountRegistrationStatus.EmailUnavailable, "EMAIL_UNAVAILABLE").ConfigureAwait(false);
            }
        }

        if (!await IsUsernameAvailableAsync(username, cancellationToken).ConfigureAwait(false))
        {
            return await RejectAsync(LocalAccountRegistrationStatus.UsernameUnavailable, "USERNAME_UNAVAILABLE").ConfigureAwait(false);
        }

        DateTimeOffset now = clock.UtcNow;
        LocalIdentityUser user = LocalIdentityUser.Create(username, email, now);
        if (initialAdministrator)
        {
            // The upstream first-run form intentionally has no email field. Mark this
            // bootstrap account confirmed so a production instance that requires email for
            // later registrations can still complete the immediate post-setup sign-in.
            user.EmailConfirmed = true;
        }
        var passwordErrors = new List<IdentityError>();
        foreach (IPasswordValidator<LocalIdentityUser> validator in users.PasswordValidators)
        {
            IdentityResult validation = await validator.ValidateAsync(users, user, password).ConfigureAwait(false);
            if (!validation.Succeeded)
            {
                passwordErrors.AddRange(validation.Errors);
            }
        }

        if (passwordErrors.Count > 0)
        {
            await ReleaseInvitationIfNeededAsync().ConfigureAwait(false);
            string[] safeCodes = passwordErrors
                .Select(MapIdentityError)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new(MapRegistrationStatus(safeCodes), null, safeCodes);
        }

        IdentityResult created;
        try
        {
            AtomicLocalAccountCreationResult atomicResult = await atomicRegistration.CreateAsync(
                user,
                password,
                admission.InvitationReservation,
                now,
                cancellationToken).ConfigureAwait(false);
            if (!atomicResult.InvitationAccepted)
            {
                return await RejectAsync(
                    LocalAccountRegistrationStatus.InvitationInvalid,
                    "INVALID_INVITATION_CODE").ConfigureAwait(false);
            }

            invitationConsumed = atomicResult.InvitationConsumed;
            created = atomicResult.IdentityResult;
        }
        catch (DbUpdateException exception)
        {
            // The availability checks are advisory. PostgreSQL's unique indexes are the
            // authority when two registrations race. Detach the failed insert before querying
            // the committed winner so the losing request becomes a stable contract error,
            // without translating unrelated storage failures into validation failures.
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            if (await users.FindByNameAsync(username).ConfigureAwait(false) is not null)
            {
                LogRegistrationCollision(logger, user.Id, "username");
                return await RejectAsync(
                    LocalAccountRegistrationStatus.UsernameUnavailable,
                    "USERNAME_UNAVAILABLE").ConfigureAwait(false);
            }

            if (email is not null && await users.FindByEmailAsync(email).ConfigureAwait(false) is not null)
            {
                LogRegistrationCollision(logger, user.Id, "email");
                return await RejectAsync(
                    LocalAccountRegistrationStatus.EmailUnavailable,
                    "EMAIL_UNAVAILABLE").ConfigureAwait(false);
            }

            await ReleaseInvitationIfNeededAsync().ConfigureAwait(false);
            throw;
        }

        if (!created.Succeeded)
        {
            await ReleaseInvitationIfNeededAsync().ConfigureAwait(false);
            string[] safeCodes = created.Errors.Select(MapIdentityError).Distinct(StringComparer.Ordinal).ToArray();
            return new(MapRegistrationStatus(safeCodes), null, safeCodes);
        }

        if (initialAdministrator)
        {
            const string administratorRole = "activitypub-admin";
            if (!await roles.RoleExistsAsync(administratorRole).ConfigureAwait(false))
            {
                IdentityResult roleCreated = await roles.CreateAsync(new LocalIdentityRole
                {
                    Name = administratorRole
                }).ConfigureAwait(false);
                if (!roleCreated.Succeeded)
                {
                    return Failure(
                        LocalAccountRegistrationStatus.ProvisioningFailed,
                        "ADMINISTRATOR_ROLE_PROVISIONING_FAILED",
                        user);
                }
            }

            IdentityResult roleAssigned = await users.AddToRoleAsync(user, administratorRole).ConfigureAwait(false);
            if (!roleAssigned.Succeeded)
            {
                return Failure(
                    LocalAccountRegistrationStatus.ProvisioningFailed,
                    "ADMINISTRATOR_ROLE_ASSIGNMENT_FAILED",
                    user);
            }
        }

        user.BeginProvisioning(now);
        IdentityResult provisioningStarted = await users.UpdateAsync(user).ConfigureAwait(false);
        if (!provisioningStarted.Succeeded)
        {
            await MarkProvisioningFailedAsync(user, "IDENTITY_STATE_CONFLICT", now).ConfigureAwait(false);
            return Failure(LocalAccountRegistrationStatus.ProvisioningFailed, "IDENTITY_STATE_CONFLICT", user);
        }

        try
        {
            LocalActorAdministrationResult actor = await actorAdministration.CreateAsync(
                username,
                ActorKind.Person,
                username,
                string.Empty,
                manuallyApprovesFollowers: false,
                discoverable: true,
                indexable: true,
                operatorId: $"registration:{user.Id:N}",
                cancellationToken).ConfigureAwait(false);
            DateTimeOffset activatedAt = clock.UtcNow;
            user.Activate(actor.ActorId, actor.ActorIri, activatedAt);
            IdentityResult activated = await users.UpdateAsync(user).ConfigureAwait(false);
            if (!activated.Succeeded)
            {
                await MarkProvisioningFailedAsync(user, "IDENTITY_ACTIVATION_CONFLICT", activatedAt).ConfigureAwait(false);
                return Failure(LocalAccountRegistrationStatus.ProvisioningFailed, "IDENTITY_ACTIVATION_CONFLICT", user);
            }

            await audit.AppendAsync(
                "identity",
                "local-account-activated",
                user.Id.ToString("N"),
                actor.ActorIri,
                JsonSerializer.Serialize(new { userId = user.Id, actorId = actor.ActorId }),
                activatedAt,
                cancellationToken).ConfigureAwait(false);
            return new(LocalAccountRegistrationStatus.Created, user, []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProvisioningFailure(logger, user.Id, exception.GetType().Name, exception);
            await MarkProvisioningFailedAsync(user, "ACTOR_PROVISIONING_FAILED", clock.UtcNow).ConfigureAwait(false);
            return Failure(LocalAccountRegistrationStatus.ProvisioningFailed, "ACTOR_PROVISIONING_FAILED", user);
        }
    }

    public async Task<LocalAccountAuthenticationResult> AuthenticatePasswordAsync(
        string username,
        string password,
        string? authenticatorCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new(LocalAccountAuthenticationStatus.InvalidCredentials, null);
        }

        LocalIdentityUser? user = await users.FindByNameAsync(username.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            passwordTimingEqualizer.VerifyUnknownPassword(password);
            return new(LocalAccountAuthenticationStatus.InvalidCredentials, null);
        }

        SignInResult result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true).ConfigureAwait(false);
        if (result.IsLockedOut)
        {
            return new(LocalAccountAuthenticationStatus.LockedOut, null);
        }

        if (!result.Succeeded)
        {
            return new(LocalAccountAuthenticationStatus.InvalidCredentials, null);
        }

        if (user.ProvisioningState != LocalAccountProvisioningState.Active || string.IsNullOrWhiteSpace(user.LocalActorIri))
        {
            return new(LocalAccountAuthenticationStatus.AccountNotActive, null);
        }

        if (options.RequireConfirmedEmail && !await users.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return new(LocalAccountAuthenticationStatus.EmailConfirmationRequired, null);
        }

        if (user.TwoFactorEnabled)
        {
            string normalizedCode = (authenticatorCode ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (normalizedCode.Length == 0)
            {
                return new(LocalAccountAuthenticationStatus.TwoFactorRequired, user);
            }

            if (normalizedCode.Length != 6 || normalizedCode.Any(character => !char.IsAsciiDigit(character)) ||
                !await users.VerifyTwoFactorTokenAsync(
                    user,
                    TokenOptions.DefaultAuthenticatorProvider,
                    normalizedCode).ConfigureAwait(false))
            {
                await users.AccessFailedAsync(user).ConfigureAwait(false);
                return new(LocalAccountAuthenticationStatus.InvalidSecondFactor, null);
            }

            await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        }

        user.RecordSignIn(clock.UtcNow);
        IdentityResult updated = await users.UpdateAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            LogSignInTimestampFailure(logger, user.Id);
        }

        return new(LocalAccountAuthenticationStatus.Succeeded, user);
    }

    public async Task<LocalPasskeyChallengeResult> BeginPasskeyAuthenticationAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new(LocalPasskeyChallengeStatus.InvalidCredentials, null);
        }

        LocalIdentityUser? user = await users.FindByNameAsync(username.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            passwordTimingEqualizer.VerifyUnknownPassword(password);
            return new(LocalPasskeyChallengeStatus.InvalidCredentials, null);
        }

        SignInResult passwordResult = await signIn
            .CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (passwordResult.IsLockedOut)
        {
            return new(LocalPasskeyChallengeStatus.LockedOut, null);
        }

        if (!passwordResult.Succeeded)
        {
            return new(LocalPasskeyChallengeStatus.InvalidCredentials, null);
        }

        if (user.ProvisioningState != LocalAccountProvisioningState.Active || string.IsNullOrWhiteSpace(user.LocalActorIri))
        {
            return new(LocalPasskeyChallengeStatus.AccountNotActive, null);
        }

        if (options.RequireConfirmedEmail && !await users.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return new(LocalPasskeyChallengeStatus.EmailConfirmationRequired, null);
        }

        if (!user.TwoFactorEnabled || !users.SupportsUserPasskey ||
            (await users.GetPasskeysAsync(user).ConfigureAwait(false)).Count == 0)
        {
            return new(LocalPasskeyChallengeStatus.PasskeyUnavailable, null);
        }

        string requestOptionsJson = await signIn.MakePasskeyRequestOptionsAsync(user).ConfigureAwait(false);
        return new(LocalPasskeyChallengeStatus.Created, requestOptionsJson);
    }

    public async Task<LocalPasskeyAuthenticationResult> AuthenticatePasskeyAsync(
        string credentialJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled || string.IsNullOrWhiteSpace(credentialJson) || credentialJson.Length > 12_288)
        {
            return new(LocalPasskeyAuthenticationStatus.InvalidAssertion, null);
        }

        PasskeyAssertionResult<LocalIdentityUser> assertion;
        try
        {
            assertion = await signIn.PerformPasskeyAssertionAsync(credentialJson).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException or PasskeyException)
        {
            // The protected assertion state is single-use. A missing, expired, replayed, or
            // operation-mismatched state is deliberately indistinguishable from a bad assertion.
            return new(LocalPasskeyAuthenticationStatus.InvalidAssertion, null);
        }

        if (!assertion.Succeeded || assertion.User is null || assertion.Passkey is null)
        {
            return new(LocalPasskeyAuthenticationStatus.InvalidAssertion, null);
        }

        LocalIdentityUser user = assertion.User;
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return new(LocalPasskeyAuthenticationStatus.LockedOut, null);
        }

        if (user.ProvisioningState != LocalAccountProvisioningState.Active || string.IsNullOrWhiteSpace(user.LocalActorIri))
        {
            return new(LocalPasskeyAuthenticationStatus.AccountNotActive, null);
        }

        if (options.RequireConfirmedEmail && !await users.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return new(LocalPasskeyAuthenticationStatus.EmailConfirmationRequired, null);
        }

        IdentityResult persisted = await users.AddOrUpdatePasskeyAsync(user, assertion.Passkey).ConfigureAwait(false);
        if (!persisted.Succeeded)
        {
            return new(LocalPasskeyAuthenticationStatus.PersistenceFailed, null);
        }

        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        user.RecordSignIn(clock.UtcNow);
        IdentityResult updated = await users.UpdateAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            LogSignInTimestampFailure(logger, user.Id);
        }

        return new(LocalPasskeyAuthenticationStatus.Succeeded, user);
    }

    private async Task MarkProvisioningFailedAsync(LocalIdentityUser user, string code, DateTimeOffset now)
    {
        user.FailProvisioning(code, now);
        IdentityResult result = await users.UpdateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded && logger.IsEnabled(LogLevel.Critical))
        {
            int errorCount = result.Errors.Count();
            LogProvisioningStatePersistenceFailure(logger, user.Id, errorCount);
        }
    }

    private static LocalAccountRegistrationResult Failure(
        LocalAccountRegistrationStatus status,
        string code,
        LocalIdentityUser? user = null) => new(status, user, [code]);

    private static string NormalizeLookupUsername(string value) => value.Trim() is { Length: <= 20 } normalized
        ? normalized
        : string.Empty;

    private static bool IsValidUsername(string value) => UsernamePattern().IsMatch(value);

    private static string MapIdentityError(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" => "USERNAME_UNAVAILABLE",
        "DuplicateEmail" => "EMAIL_UNAVAILABLE",
        "InvalidEmail" => "INVALID_EMAIL",
        "InvalidUserName" => "INVALID_USERNAME",
        "PasswordTooShort" => "PASSWORD_TOO_SHORT",
        "PasswordRequiresNonAlphanumeric" => "PASSWORD_REQUIRES_SYMBOL",
        "PasswordRequiresDigit" => "PASSWORD_REQUIRES_DIGIT",
        "PasswordRequiresLower" => "PASSWORD_REQUIRES_LOWERCASE",
        "PasswordRequiresUpper" => "PASSWORD_REQUIRES_UPPERCASE",
        "PasswordRequiresUniqueChars" => "PASSWORD_REQUIRES_UNIQUE_CHARACTERS",
        _ => "ACCOUNT_VALIDATION_FAILED"
    };

    private static LocalAccountRegistrationStatus MapRegistrationStatus(IReadOnlyCollection<string> codes)
    {
        if (codes.Contains("USERNAME_UNAVAILABLE", StringComparer.Ordinal))
        {
            return LocalAccountRegistrationStatus.UsernameUnavailable;
        }

        if (codes.Contains("EMAIL_UNAVAILABLE", StringComparer.Ordinal))
        {
            return LocalAccountRegistrationStatus.EmailUnavailable;
        }

        if (codes.Any(code => code.Contains("EMAIL", StringComparison.Ordinal)))
        {
            return LocalAccountRegistrationStatus.InvalidEmail;
        }

        if (codes.Any(code => code.StartsWith("PASSWORD_", StringComparison.Ordinal)))
        {
            return LocalAccountRegistrationStatus.InvalidPassword;
        }

        return LocalAccountRegistrationStatus.InvalidUsername;
    }

    [GeneratedRegex("^[a-zA-Z0-9_]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [LoggerMessage(
        EventId = 2401,
        Level = LogLevel.Error,
        Message = "Local account provisioning failed. UserId={UserId} FailureType={FailureType}")]
    private static partial void LogProvisioningFailure(
        ILogger logger,
        Guid userId,
        string failureType,
        Exception exception);

    [LoggerMessage(
        EventId = 2402,
        Level = LogLevel.Warning,
        Message = "Local account sign-in timestamp was not persisted. UserId={UserId}")]
    private static partial void LogSignInTimestampFailure(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 2403,
        Level = LogLevel.Critical,
        Message = "Local account provisioning failure could not be persisted. UserId={UserId} ErrorCount={ErrorCount}")]
    private static partial void LogProvisioningStatePersistenceFailure(ILogger logger, Guid userId, int errorCount);

    [LoggerMessage(
        EventId = 2404,
        Level = LogLevel.Information,
        Message = "Concurrent local account registration was rejected by a database uniqueness constraint. AttemptUserId={AttemptUserId} Collision={Collision}")]
    private static partial void LogRegistrationCollision(ILogger logger, Guid attemptUserId, string collision);
}
