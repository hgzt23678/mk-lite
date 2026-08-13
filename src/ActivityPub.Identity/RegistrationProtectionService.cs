using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Identity;

public sealed class RegistrationInvitationService(
    IRegistrationInvitationStore store,
    IClock clock,
    RegistrationProtectionOptions options) : IRegistrationInvitationService
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 26;

    public async Task<RegistrationInvitationIssueResult> IssueAsync(
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (!options.InvitationRequired)
        {
            throw new InvalidOperationException("Invitation-only registration is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 256 || operatorId.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(nameof(operatorId));
        }

        DateTimeOffset createdAt = clock.UtcNow;
        DateTimeOffset expiresAt = createdAt.Add(options.InvitationLifetime);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string code = CreateCode();
            byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(code));
            if (await store.CreateAsync(hash, operatorId, createdAt, expiresAt, cancellationToken).ConfigureAwait(false))
            {
                return new RegistrationInvitationIssueResult(code, expiresAt);
            }
        }

        throw new InvalidOperationException("A unique registration invitation could not be allocated.");
    }

    private static string CreateCode()
    {
        Span<byte> random = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(random);
        Span<char> code = stackalloc char[CodeLength];
        for (int index = 0; index < code.Length; index++)
        {
            // The alphabet has exactly 32 entries. Mapping the low five bits of a
            // uniform byte is therefore unbiased; 26 symbols retain 130 bits.
            code[index] = Alphabet[random[index] & 31];
        }

        return new string(code);
    }
}

public sealed class RegistrationProtectionService(
    IRegistrationInvitationStore invitations,
    IRegistrationCaptchaVerifier captcha,
    IClock clock,
    RegistrationProtectionOptions options) : IRegistrationProtectionService
{
    private const int InvitationCodeLength = 26;

    public async Task<RegistrationProtectionResult> AuthorizeAsync(
        LocalRegistrationProtection protection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protection);
        if (options.CaptchaProvider != RegistrationCaptchaProvider.None)
        {
            string? response = options.CaptchaProvider switch
            {
                RegistrationCaptchaProvider.Hcaptcha => protection.HcaptchaResponse,
                RegistrationCaptchaProvider.Recaptcha => protection.RecaptchaResponse,
                RegistrationCaptchaProvider.Turnstile => protection.TurnstileResponse,
                _ => null
            };
            int maximumResponseLength = options.CaptchaProvider == RegistrationCaptchaProvider.Turnstile
                ? 2_048
                : 8_192;
            if (string.IsNullOrWhiteSpace(response) || response.Length > maximumResponseLength)
            {
                return new(RegistrationProtectionStatus.CaptchaInvalid, null);
            }

            RegistrationCaptchaVerificationResult verified = await captcha
                .VerifyAsync(options.CaptchaProvider, response, protection.RemoteIpAddress, cancellationToken)
                .ConfigureAwait(false);
            if (verified != RegistrationCaptchaVerificationResult.Valid)
            {
                return new(
                    verified == RegistrationCaptchaVerificationResult.Unavailable
                        ? RegistrationProtectionStatus.CaptchaUnavailable
                        : RegistrationProtectionStatus.CaptchaInvalid,
                    null);
            }
        }

        if (!options.InvitationRequired)
        {
            return new(RegistrationProtectionStatus.Accepted, null);
        }

        string invitationCode = protection.InvitationCode ?? string.Empty;
        if (invitationCode.Length != InvitationCodeLength ||
            invitationCode.Any(character => !AlphabetContains(character)))
        {
            return new(RegistrationProtectionStatus.InvitationInvalid, null);
        }

        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(invitationCode));
        DateTimeOffset now = clock.UtcNow;
        RegistrationInvitationReservation? reservation = await invitations.ReserveAsync(
            hash,
            now,
            now.Add(options.InvitationReservationLifetime),
            cancellationToken).ConfigureAwait(false);
        return reservation is null
            ? new(RegistrationProtectionStatus.InvitationInvalid, null)
            : new(RegistrationProtectionStatus.Accepted, reservation);
    }

    public Task<bool> ConsumeInvitationAsync(
        RegistrationInvitationReservation reservation,
        string username,
        CancellationToken cancellationToken) =>
        invitations.ConsumeAsync(reservation, username, clock.UtcNow, cancellationToken);

    public Task ReleaseInvitationAsync(
        RegistrationInvitationReservation reservation,
        CancellationToken cancellationToken) => invitations.ReleaseAsync(reservation, cancellationToken);

    private static bool AlphabetContains(char character) =>
        character is >= '2' and <= '9' or >= 'A' and <= 'H' or >= 'J' and <= 'N' or >= 'P' and <= 'Z';
}

public sealed partial class RegistrationCaptchaVerifier(
    HttpClient client,
    RegistrationProtectionOptions options,
    ILogger<RegistrationCaptchaVerifier> logger) : IRegistrationCaptchaVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri HcaptchaVerificationUri = new("https://api.hcaptcha.com/siteverify");
    private static readonly Uri RecaptchaVerificationUri = new("https://www.google.com/recaptcha/api/siteverify");
    private static readonly Uri TurnstileVerificationUri = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<RegistrationCaptchaVerificationResult> VerifyAsync(
        RegistrationCaptchaProvider provider,
        string response,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        int maximumResponseLength = provider == RegistrationCaptchaProvider.Turnstile ? 2_048 : 8_192;
        if (provider == RegistrationCaptchaProvider.None || string.IsNullOrWhiteSpace(response) ||
            response.Length > maximumResponseLength)
        {
            return RegistrationCaptchaVerificationResult.Invalid;
        }

        string secret;
        try
        {
            secret = await ReadBoundedSecretAsync(options.CaptchaSecretFile!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            LogProviderFailure(logger, provider.ToString(), "secret-unavailable");
            return RegistrationCaptchaVerificationResult.Unavailable;
        }

        if (secret.Length is 0 or > 4_096)
        {
            LogProviderFailure(logger, provider.ToString(), "secret-invalid");
            return RegistrationCaptchaVerificationResult.Unavailable;
        }

        Uri endpoint = provider switch
        {
            RegistrationCaptchaProvider.Hcaptcha => HcaptchaVerificationUri,
            RegistrationCaptchaProvider.Recaptcha => RecaptchaVerificationUri,
            RegistrationCaptchaProvider.Turnstile => TurnstileVerificationUri,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        var fields = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = response
        };
        if (provider == RegistrationCaptchaProvider.Hcaptcha)
        {
            fields["sitekey"] = options.CaptchaSiteKey;
        }
        else if (provider == RegistrationCaptchaProvider.Turnstile)
        {
            fields["idempotency_key"] = Guid.NewGuid().ToString("D");
        }

        if (System.Net.IPAddress.TryParse(remoteIpAddress, out System.Net.IPAddress? remoteAddress) &&
            !System.Net.IPAddress.IsLoopback(remoteAddress))
        {
            fields["remoteip"] = remoteAddress.ToString();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CaptchaVerificationTimeout);
        try
        {
            int maximumAttempts = provider == RegistrationCaptchaProvider.Turnstile ? 2 : 1;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new FormUrlEncodedContent(fields)
                };
                HttpResponseMessage result;
                try
                {
                    result = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (
                    provider == RegistrationCaptchaProvider.Turnstile && attempt < maximumAttempts)
                {
                    continue;
                }

                using (result)
                {
                    if (result.StatusCode != HttpStatusCode.OK ||
                        result.Content.Headers.ContentLength is > 32_768)
                    {
                        if (provider == RegistrationCaptchaProvider.Turnstile && attempt < maximumAttempts &&
                            IsTransientTurnstileStatus(result.StatusCode))
                        {
                            continue;
                        }

                        LogProviderFailure(logger, provider.ToString(), $"http-{(int)result.StatusCode}");
                        return RegistrationCaptchaVerificationResult.Unavailable;
                    }

                    CaptchaResponse? payload = await ReadBoundedResponseAsync(result.Content, timeout.Token).ConfigureAwait(false);
                    bool hostnameMatches = string.IsNullOrWhiteSpace(options.CaptchaExpectedHostname) ||
                        string.Equals(payload?.Hostname, options.CaptchaExpectedHostname, StringComparison.OrdinalIgnoreCase);
                    bool siteKeyMatches = provider != RegistrationCaptchaProvider.Hcaptcha ||
                        string.IsNullOrWhiteSpace(payload?.SiteKey) ||
                        string.Equals(payload.SiteKey, options.CaptchaSiteKey, StringComparison.Ordinal);
                    bool actionMatches = provider != RegistrationCaptchaProvider.Turnstile ||
                        string.Equals(payload?.Action, options.CaptchaExpectedAction, StringComparison.Ordinal);
                    bool cdataMatches = provider != RegistrationCaptchaProvider.Turnstile ||
                        string.Equals(payload?.Cdata, options.CaptchaExpectedCdata, StringComparison.Ordinal);
                    return payload?.Success == true && hostnameMatches && siteKeyMatches && actionMatches && cdataMatches
                        ? RegistrationCaptchaVerificationResult.Valid
                        : RegistrationCaptchaVerificationResult.Invalid;
                }
            }

            return RegistrationCaptchaVerificationResult.Unavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderFailure(logger, provider.ToString(), "timeout");
            return RegistrationCaptchaVerificationResult.Unavailable;
        }
        catch (HttpRequestException)
        {
            LogProviderFailure(logger, provider.ToString(), "request-failed");
            return RegistrationCaptchaVerificationResult.Unavailable;
        }
        catch (JsonException)
        {
            LogProviderFailure(logger, provider.ToString(), "invalid-json");
            return RegistrationCaptchaVerificationResult.Unavailable;
        }
        finally
        {
            secret = string.Empty;
        }
    }

    private static bool IsTransientTurnstileStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode == 425 || (int)statusCode >= 500;

    private sealed class CaptchaResponse
    {
        public bool Success { get; init; }

        public string? Hostname { get; init; }

        public string? SiteKey { get; init; }

        public string? Action { get; init; }

        public string? Cdata { get; init; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; init; }
    }

    private static async Task<string> ReadBoundedSecretAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4_096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is 0 or > 4_096)
        {
            throw new InvalidDataException("The captcha secret file is empty or oversized.");
        }

        byte[] buffer = new byte[4_097];
        try
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read is 0 or > 4_096)
            {
                throw new InvalidDataException("The captcha secret file is empty or oversized.");
            }

            return new UTF8Encoding(false, true).GetString(buffer, 0, read).TrimEnd('\r', '\n');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static async Task<CaptchaResponse?> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[8_192];
        using var destination = new MemoryStream(capacity: 32_768);
        while (true)
        {
            int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            if (destination.Length + count > 32_768)
            {
                throw new JsonException("Captcha verification response exceeds the allowed size.");
            }

            destination.Write(buffer, 0, count);
        }

        return JsonSerializer.Deserialize<CaptchaResponse>(
            destination.GetBuffer().AsSpan(0, checked((int)destination.Length)),
            JsonOptions);
    }

    [LoggerMessage(LogLevel.Warning, "Registration captcha provider {Provider} failed with safe reason {Reason}.")]
    private static partial void LogProviderFailure(ILogger logger, string provider, string reason);
}
