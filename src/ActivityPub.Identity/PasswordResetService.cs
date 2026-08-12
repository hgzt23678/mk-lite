using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Identity;

public sealed partial class PasswordResetService(
    UserManager<LocalIdentityUser> users,
    IPasswordResetStore store,
    IPasswordResetEmailSender emailSender,
    IAuditLog audit,
    IClock clock,
    PasswordResetOptions options,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public async Task RequestAsync(
        string username,
        string email,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frontendPublicBaseUri);
        if (!options.Enabled || string.IsNullOrWhiteSpace(username) || username.Length > 20 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return;
        }

        LocalIdentityUser? user = await users.FindByNameAsync(username.Trim()).ConfigureAwait(false);
        if (user is null || user.ProvisioningState != LocalAccountProvisioningState.Active ||
            !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        string normalizedSubmittedEmail = users.NormalizeEmail(email.Trim());
        string normalizedStoredEmail = users.NormalizeEmail(user.Email);
        if (!string.Equals(normalizedSubmittedEmail, normalizedStoredEmail, StringComparison.Ordinal))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string identityToken = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        string externalToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(identityToken));
        byte[] tokenHash = HashToken(externalToken);
        DateTimeOffset requestedAt = clock.UtcNow;
        DateTimeOffset expiresAt = requestedAt.Add(options.TokenLifetime);
        bool reserved = await store.TryReserveAsync(
            user.Id,
            tokenHash,
            requestedAt,
            expiresAt,
            options.RequestCooldown,
            cancellationToken).ConfigureAwait(false);
        if (!reserved)
        {
            return;
        }

        Uri resetUri = BuildResetUri(frontendPublicBaseUri, externalToken);
        try
        {
            await emailSender.SendAsync(
                new PasswordResetEmail(user.Email, resetUri, expiresAt),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ReleaseReservationAsync(user.Id, tokenHash).ConfigureAwait(false);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            LogEmailFailure(logger, user.Id, exception.GetType().Name, exception);
            return;
        }

        try
        {
            await audit.AppendAsync(
                "identity",
                "password-reset-requested",
                user.Id.ToString("N"),
                user.Id.ToString("N"),
                JsonSerializer.Serialize(new { userId = user.Id, expiresAt }),
                requestedAt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailure(logger, user.Id, "password-reset-requested", exception);
        }
    }

    public async Task<PasswordResetCompletionResult> ResetAsync(
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Failure(PasswordResetCompletionStatus.Disabled, "PASSWORD_RESET_DISABLED");
        }

        if (!IsBoundedToken(token) || string.IsNullOrEmpty(password) || password.Length > 1_024)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        byte[] decodedToken;
        try
        {
            decodedToken = WebEncoders.Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        string identityToken;
        try
        {
            identityToken = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(decodedToken);
        }
        catch (DecoderFallbackException)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        byte[] tokenHash = HashToken(token);
        DateTimeOffset now = clock.UtcNow;
        Guid? userId = await store.FindActiveUserIdAsync(tokenHash, now, cancellationToken).ConfigureAwait(false);
        if (userId is null)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        LocalIdentityUser? user = await users.FindByIdAsync(userId.Value.ToString()).ConfigureAwait(false);
        if (user is null || user.ProvisioningState != LocalAccountProvisioningState.Active)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        var validationErrors = new List<IdentityError>();
        foreach (IPasswordValidator<LocalIdentityUser> validator in users.PasswordValidators)
        {
            IdentityResult validation = await validator.ValidateAsync(users, user, password).ConfigureAwait(false);
            if (!validation.Succeeded)
            {
                validationErrors.AddRange(validation.Errors);
            }
        }

        string[] safeValidationErrors = validationErrors
            .Select(MapPasswordError)
            .Where(code => code is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (safeValidationErrors.Length > 0)
        {
            return new PasswordResetCompletionResult(PasswordResetCompletionStatus.InvalidPassword, safeValidationErrors);
        }

        Guid? claimedUserId = await store.TryClaimAsync(tokenHash, now, cancellationToken).ConfigureAwait(false);
        if (claimedUserId != userId)
        {
            return Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        IdentityResult result = await users.ResetPasswordAsync(user, identityToken, password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            string[] passwordErrors = result.Errors
                .Select(MapPasswordError)
                .Where(code => code is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return passwordErrors.Length > 0
                ? new PasswordResetCompletionResult(PasswordResetCompletionStatus.InvalidPassword, passwordErrors)
                : Failure(PasswordResetCompletionStatus.InvalidOrExpiredToken, "INVALID_OR_EXPIRED_TOKEN");
        }

        DateTimeOffset resetAt = clock.UtcNow;
        try
        {
            await audit.AppendAsync(
                "identity",
                "password-reset-completed",
                user.Id.ToString("N"),
                user.Id.ToString("N"),
                JsonSerializer.Serialize(new { userId = user.Id }),
                resetAt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailure(logger, user.Id, "password-reset-completed", exception);
        }

        return new PasswordResetCompletionResult(PasswordResetCompletionStatus.Succeeded, []);
    }

    private static Uri BuildResetUri(Uri publicBaseUri, string token)
    {
        var builder = new UriBuilder(new Uri(publicBaseUri, "/reset-password"))
        {
            Fragment = token
        };
        return builder.Uri;
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    private static bool IsBoundedToken(string token) => token.Length is >= 32 and <= 8_192 &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string? MapPasswordError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "PASSWORD_TOO_SHORT",
        "PasswordRequiresNonAlphanumeric" => "PASSWORD_REQUIRES_SYMBOL",
        "PasswordRequiresDigit" => "PASSWORD_REQUIRES_DIGIT",
        "PasswordRequiresLower" => "PASSWORD_REQUIRES_LOWERCASE",
        "PasswordRequiresUpper" => "PASSWORD_REQUIRES_UPPERCASE",
        "PasswordRequiresUniqueChars" => "PASSWORD_REQUIRES_UNIQUE_CHARACTERS",
        _ => null
    };

    private static PasswordResetCompletionResult Failure(PasswordResetCompletionStatus status, string code) =>
        new(status, [code]);

    private async Task ReleaseReservationAsync(Guid userId, byte[] tokenHash)
    {
        try
        {
            await store.ReleaseAsync(userId, tokenHash, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReservationReleaseFailure(logger, userId, exception);
        }
    }

    [LoggerMessage(
        EventId = 2411,
        Level = LogLevel.Error,
        Message = "Password reset email delivery failed. UserId={UserId} FailureType={FailureType}")]
    private static partial void LogEmailFailure(
        ILogger logger,
        Guid userId,
        string failureType,
        Exception exception);

    [LoggerMessage(
        EventId = 2412,
        Level = LogLevel.Error,
        Message = "Password reset audit persistence failed. UserId={UserId} Action={Action}")]
    private static partial void LogAuditFailure(
        ILogger logger,
        Guid userId,
        string action,
        Exception exception);

    [LoggerMessage(
        EventId = 2413,
        Level = LogLevel.Error,
        Message = "Password reset reservation could not be released after email delivery failure. UserId={UserId}")]
    private static partial void LogReservationReleaseFailure(ILogger logger, Guid userId, Exception exception);
}
