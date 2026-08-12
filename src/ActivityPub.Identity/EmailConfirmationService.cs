using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Identity;

public sealed partial class EmailConfirmationService(
    UserManager<LocalIdentityUser> users,
    IEmailConfirmationStore store,
    IEmailConfirmationSender sender,
    IAuditLog audit,
    IClock clock,
    PasswordResetOptions options,
    ILogger<EmailConfirmationService> logger) : IEmailConfirmationService
{
    public async Task RequestForUserAsync(
        LocalIdentityUser user,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(frontendPublicBaseUri);
        if (!options.Enabled || user.ProvisioningState != LocalAccountProvisioningState.Active ||
            user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string identityToken = await users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        string externalToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(identityToken));
        byte[] tokenHash = HashToken(externalToken);
        DateTimeOffset requestedAt = clock.UtcNow;
        DateTimeOffset expiresAt = requestedAt.Add(options.EmailConfirmationTokenLifetime);
        if (!await store.TryReserveAsync(
                user.Id,
                tokenHash,
                requestedAt,
                expiresAt,
                options.EmailConfirmationRequestCooldown,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Uri confirmationUri = BuildConfirmationUri(frontendPublicBaseUri, externalToken);
        try
        {
            await sender.SendAsync(
                new EmailConfirmationEmail(user.Email, confirmationUri, expiresAt),
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

        await AppendAuditBestEffortAsync(
            "email-confirmation-requested",
            user.Id,
            JsonSerializer.Serialize(new { userId = user.Id, expiresAt }),
            requestedAt,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RequestAsync(
        string username,
        string email,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(username) || username.Length > 20 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return;
        }

        LocalIdentityUser? user = await users.FindByNameAsync(username.Trim()).ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || user.EmailConfirmed ||
            !string.Equals(users.NormalizeEmail(user.Email), users.NormalizeEmail(email.Trim()), StringComparison.Ordinal))
        {
            return;
        }

        await RequestForUserAsync(user, frontendPublicBaseUri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailConfirmationResult> ConfirmAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new(EmailConfirmationStatus.Disabled, null);
        }

        if (!IsBoundedToken(token))
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        byte[] decoded;
        try
        {
            decoded = WebEncoders.Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        string identityToken;
        try
        {
            identityToken = new UTF8Encoding(false, true).GetString(decoded);
        }
        catch (DecoderFallbackException)
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        byte[] tokenHash = HashToken(token);
        DateTimeOffset confirmedAt = clock.UtcNow;
        Guid? userId = await store.TryClaimAsync(tokenHash, confirmedAt, cancellationToken).ConfigureAwait(false);
        if (userId is null)
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        LocalIdentityUser? user = await users.FindByIdAsync(userId.Value.ToString()).ConfigureAwait(false);
        if (user is null || user.EmailConfirmed || user.ProvisioningState != LocalAccountProvisioningState.Active)
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        IdentityResult result = await users.ConfirmEmailAsync(user, identityToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new(EmailConfirmationStatus.InvalidOrExpiredToken, null);
        }

        await AppendAuditBestEffortAsync(
            "email-confirmation-completed",
            user.Id,
            JsonSerializer.Serialize(new { userId = user.Id }),
            confirmedAt,
            cancellationToken).ConfigureAwait(false);
        return new(EmailConfirmationStatus.Succeeded, user);
    }

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

    private async Task AppendAuditBestEffortAsync(
        string action,
        Guid userId,
        string details,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await audit.AppendAsync(
                "identity",
                action,
                userId.ToString("N"),
                userId.ToString("N"),
                details,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailure(logger, userId, action, exception);
        }
    }

    private static Uri BuildConfirmationUri(Uri publicBaseUri, string token)
    {
        var builder = new UriBuilder(new Uri(publicBaseUri, "/signup-complete"))
        {
            Fragment = token
        };
        return builder.Uri;
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    private static bool IsBoundedToken(string token) => token.Length is >= 32 and <= 8_192 &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    [LoggerMessage(
        EventId = 2421,
        Level = LogLevel.Error,
        Message = "Email confirmation delivery failed. UserId={UserId} FailureType={FailureType}")]
    private static partial void LogEmailFailure(ILogger logger, Guid userId, string failureType, Exception exception);

    [LoggerMessage(
        EventId = 2422,
        Level = LogLevel.Error,
        Message = "Email confirmation reservation could not be released after delivery failure. UserId={UserId}")]
    private static partial void LogReservationReleaseFailure(ILogger logger, Guid userId, Exception exception);

    [LoggerMessage(
        EventId = 2423,
        Level = LogLevel.Error,
        Message = "Email confirmation audit persistence failed. UserId={UserId} Action={Action}")]
    private static partial void LogAuditFailure(ILogger logger, Guid userId, string action, Exception exception);
}
