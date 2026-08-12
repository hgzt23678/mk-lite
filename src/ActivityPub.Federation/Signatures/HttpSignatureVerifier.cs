using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NSign;
using NSign.Http;
using NSign.Providers;
using NSign.Signatures;

namespace ActivityPub.Federation.Signatures;

public sealed record HttpSignatureVerification(
    SignatureProfile Profile,
    string KeyIri,
    string KeyOwnerIri,
    DateTimeOffset CreatedAt,
    string ReplayFingerprint,
    string? NonceHash);

public interface IHttpSignatureVerifier
{
    Task<HttpSignatureVerification> VerifyAsync(HttpContext httpContext, byte[] rawBody, CancellationToken cancellationToken);
}

public sealed class HttpSignatureException : Exception
{
    public HttpSignatureException(string message)
        : base(message)
    {
    }

    public HttpSignatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class HttpSignatureVerifier(
    IRemoteKeyResolver keyResolver,
    IFederationInstrumentation instrumentation,
    FederationOptions options,
    IClock clock) : IHttpSignatureVerifier
{
    public async Task<HttpSignatureVerification> VerifyAsync(
        HttpContext httpContext,
        byte[] rawBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(rawBody);
        try
        {
            if (HttpMethods.IsPost(httpContext.Request.Method))
            {
                HttpContentDigestVerifier.VerifyRequired(httpContext.Request, rawBody);
            }

            HttpSignatureVerification verification = httpContext.Request.Headers.ContainsKey("Signature-Input")
                ? await VerifyRfc9421Async(httpContext, cancellationToken).ConfigureAwait(false)
                : await VerifyLegacyAsync(httpContext.Request, cancellationToken).ConfigureAwait(false);
            instrumentation.SignatureVerified(verification.Profile);
            return verification;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            instrumentation.SignatureFailed(httpContext.Request.Headers.ContainsKey("Signature-Input") ? "rfc9421" : "legacy");
            throw;
        }
    }

    private async Task<HttpSignatureVerification> VerifyRfc9421Async(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.ContainsKey("Signature"))
        {
            throw new HttpSignatureException("RFC 9421 Signature header is missing.");
        }

        var verificationOptions = new SignatureVerificationOptions { ShouldVerify = _ => true };
        var messageContext = new AspNetRequestSignatureContext(httpContext.Request, verificationOptions);

        SignatureContext[] signatures;
        try
        {
            signatures = messageContext.SignaturesForVerification.ToArray();
        }
        catch (Exception exception) when (exception is FormatException or SignatureInputException or StructuredFieldParsingException)
        {
            throw new HttpSignatureException("RFC 9421 signature fields are malformed.", exception);
        }

        if (signatures.Length != 1)
        {
            throw new HttpSignatureException("Exactly one RFC 9421 signature is required.");
        }

        SignatureContext signature = signatures[0];
        SignatureParamsComponent parameters = signature.SignatureParams;
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset created = parameters.Created ?? throw new HttpSignatureException("RFC 9421 created parameter is required.");
        DateTimeOffset expires = parameters.Expires ?? throw new HttpSignatureException("RFC 9421 expires parameter is required.");
        string keyIri = parameters.KeyId ?? throw new HttpSignatureException("RFC 9421 keyid parameter is required.");
        if (!string.Equals(parameters.Algorithm, Constants.SignatureAlgorithms.RsaPkcs15Sha256, StringComparison.Ordinal))
        {
            throw new HttpSignatureException("RFC 9421 algorithm must be rsa-v1_5-sha256.");
        }

        ValidateTime(created, expires, now);
        CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        HashSet<string> components = parameters.Components.Select(x => x.ComponentName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] required = HttpMethods.IsPost(httpContext.Request.Method)
            ? ["@method", "@target-uri", "content-digest"]
            : ["@method", "@target-uri"];
        if (required.Any(component => !components.Contains(component)))
        {
            throw new HttpSignatureException("RFC 9421 signature does not cover all required components.");
        }

        ReadOnlyMemory<byte> signatureBase;
        try
        {
            signatureBase = messageContext.GetSignatureInput(parameters, out _);
        }
        catch (Exception exception) when (exception is SignatureInputException or InvalidOperationException)
        {
            throw new HttpSignatureException("RFC 9421 signature base could not be reconstructed.", exception);
        }

        RemotePublicKey key = await ResolveAndVerifyRfcAsync(
            keyIri,
            parameters,
            signatureBase,
            signature.Signature,
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false);
        string fingerprint = Fingerprint(keyIri, signatureBase.Span, signature.Signature.Span);
        return new(
            SignatureProfile.Rfc9421,
            keyIri,
            key.OwnerIri,
            created,
            fingerprint,
            parameters.Nonce is null ? null : PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(parameters.Nonce)));
    }

    private async Task<RemotePublicKey> ResolveAndVerifyRfcAsync(
        string keyIri,
        SignatureParamsComponent parameters,
        ReadOnlyMemory<byte> signatureBase,
        ReadOnlyMemory<byte> signature,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        RemotePublicKey key = await keyResolver.ResolveAsync(keyIri, forceRefresh, cancellationToken).ConfigureAwait(false);
        using RSA rsa = ImportRsaPublicKey(key);
        var provider = new RsaPkcs15Sha256SignatureProvider(null!, rsa, keyIri);
        VerificationResult result = await provider.VerifyAsync(parameters, signatureBase, signature, cancellationToken).ConfigureAwait(false);
        if (result == VerificationResult.SuccessfullyVerified)
        {
            return key;
        }

        if (!forceRefresh)
        {
            return await ResolveAndVerifyRfcAsync(keyIri, parameters, signatureBase, signature, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        }

        throw new HttpSignatureException("RFC 9421 signature verification failed after one key refresh.");
    }

    private async Task<HttpSignatureVerification> VerifyLegacyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        string header = ReadLegacySignatureHeader(request);
        IReadOnlyDictionary<string, string> parameters = LegacySignatureParser.Parse(header);
        string keyIri = GetRequired(parameters, "keyId");
        string algorithm = GetRequired(parameters, "algorithm");
        if (algorithm is not ("rsa-sha256" or "hs2019" or "rsa-v1_5-sha256"))
        {
            throw new HttpSignatureException("Legacy signature algorithm is not supported.");
        }

        string signatureValue = GetRequired(parameters, "signature");
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureValue);
        }
        catch (FormatException exception)
        {
            throw new HttpSignatureException("Legacy signature is not valid Base64.", exception);
        }

        string[] headers = GetRequired(parameters, "headers")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ValidateLegacyComponents(headers, request.Method);
        DateTimeOffset created = ReadLegacyCreated(parameters, request);
        DateTimeOffset expires = parameters.TryGetValue("expires", out string? expiresValue)
            ? ParseUnixSeconds(expiresValue, "expires")
            : created.Add(options.SignatureClockSkew);
        ValidateTime(created, expires, clock.UtcNow);
        string signingString = BuildLegacySigningString(request, headers, parameters);
        byte[] signingBytes = Encoding.ASCII.GetBytes(signingString);
        RemotePublicKey key = await ResolveAndVerifyLegacyAsync(keyIri, signingBytes, signature, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        return new(
            SignatureProfile.LegacyCavage,
            keyIri,
            key.OwnerIri,
            created,
            Fingerprint(keyIri, signingBytes, signature),
            null);
    }

    private async Task<RemotePublicKey> ResolveAndVerifyLegacyAsync(
        string keyIri,
        byte[] signingBytes,
        byte[] signature,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        RemotePublicKey key = await keyResolver.ResolveAsync(keyIri, forceRefresh, cancellationToken).ConfigureAwait(false);
        using RSA rsa = ImportRsaPublicKey(key);
        if (rsa.VerifyData(signingBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return key;
        }

        if (!forceRefresh)
        {
            return await ResolveAndVerifyLegacyAsync(keyIri, signingBytes, signature, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        }

        throw new HttpSignatureException("Legacy HTTP signature verification failed after one key refresh.");
    }

    private void ValidateTime(DateTimeOffset created, DateTimeOffset expires, DateTimeOffset now)
    {
        if (expires <= created || created > now.Add(options.SignatureClockSkew) ||
            created < now.Subtract(options.SignatureClockSkew) || expires < now.Subtract(options.SignatureClockSkew))
        {
            throw new HttpSignatureException("HTTP signature timestamp is outside the accepted window.");
        }
    }

    private static RSA ImportRsaPublicKey(RemotePublicKey key)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(key.PublicKeyPem);
            if (rsa.KeySize < 2_048)
            {
                throw new HttpSignatureException("Remote RSA key is smaller than 2048 bits.");
            }

            return rsa;
        }
        catch (CryptographicException exception)
        {
            rsa.Dispose();
            throw new HttpSignatureException("Remote public key is not a valid RSA PEM key.", exception);
        }
    }

    private static string ReadLegacySignatureHeader(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Signature", out StringValues signature))
        {
            return SingleHeader(signature, "Signature");
        }

        if (request.Headers.TryGetValue("Authorization", out StringValues authorization))
        {
            string value = SingleHeader(authorization, "Authorization");
            if (value.StartsWith("Signature ", StringComparison.OrdinalIgnoreCase))
            {
                return value[10..];
            }
        }

        throw new HttpSignatureException("Legacy HTTP Signature header is missing.");
    }

    private static void ValidateLegacyComponents(IEnumerable<string> headers, string method)
    {
        HashSet<string> covered = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!covered.Contains("(request-target)") || !covered.Contains("host") ||
            (HttpMethods.IsPost(method) && !covered.Contains("digest")))
        {
            throw new HttpSignatureException("Legacy signature does not cover request-target, host, and POST digest.");
        }

        if (!covered.Contains("date") && !covered.Contains("(created)"))
        {
            throw new HttpSignatureException("Legacy signature does not cover date or (created).");
        }
    }

    private static DateTimeOffset ReadLegacyCreated(IReadOnlyDictionary<string, string> parameters, HttpRequest request)
    {
        if (parameters.TryGetValue("created", out string? created))
        {
            return ParseUnixSeconds(created, "created");
        }

        if (!request.Headers.TryGetValue("Date", out StringValues date) ||
            !DateTimeOffset.TryParseExact(SingleHeader(date, "Date"), "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
        {
            throw new HttpSignatureException("A valid signed RFC 1123 Date header is required.");
        }

        return timestamp.ToUniversalTime();
    }

    private static DateTimeOffset ParseUnixSeconds(string value, string name)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            throw new HttpSignatureException($"Legacy {name} parameter is invalid.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new HttpSignatureException($"Legacy {name} parameter is outside the supported range.", exception);
        }
    }

    private static string BuildLegacySigningString(
        HttpRequest request,
        IEnumerable<string> headers,
        IReadOnlyDictionary<string, string> parameters)
    {
        var lines = new List<string>();
        foreach (string originalName in headers)
        {
            string name = originalName.ToLowerInvariant();
            string value = name switch
            {
                "(request-target)" => request.Method.ToLowerInvariant() + " " + request.PathBase + request.Path + request.QueryString,
                "(created)" => GetRequired(parameters, "created"),
                "(expires)" => GetRequired(parameters, "expires"),
                "host" => request.Host.ToUriComponent(),
                _ => ReadJoinedHeader(request, name)
            };
            lines.Add(name + ": " + value);
        }

        return string.Join('\n', lines);
    }

    private static string ReadJoinedHeader(HttpRequest request, string name)
    {
        if (!request.Headers.TryGetValue(name, out StringValues values) || values.Count == 0)
        {
            throw new HttpSignatureException($"Signed header '{name}' is missing.");
        }

        return string.Join(", ", values.ToArray());
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new HttpSignatureException($"HTTP signature parameter '{name}' is required.");
        }

        return value;
    }

    private static string SingleHeader(StringValues values, string name)
    {
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 16_384)
        {
            throw new HttpSignatureException($"{name} must occur exactly once and have a bounded value.");
        }

        return values[0]!;
    }

    private static string Fingerprint(string keyIri, ReadOnlySpan<byte> signingBytes, ReadOnlySpan<byte> signature)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(keyIri));
        hash.AppendData(signingBytes);
        hash.AppendData(signature);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

internal static class LegacySignatureParser
{
    public static IReadOnlyDictionary<string, string> Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int position = 0;
        while (position < value.Length)
        {
            SkipWhitespaceAndCommas(value, ref position);
            int nameStart = position;
            while (position < value.Length && (char.IsAsciiLetterOrDigit(value[position]) || value[position] is '-' or '_'))
            {
                position++;
            }

            if (position == nameStart)
            {
                throw new HttpSignatureException("Legacy signature parameter name is malformed.");
            }

            string name = value[nameStart..position];
            SkipWhitespace(value, ref position);
            if (position >= value.Length || value[position++] != '=')
            {
                throw new HttpSignatureException("Legacy signature parameter has no equals sign.");
            }

            SkipWhitespace(value, ref position);
            if (position >= value.Length || value[position++] != '"')
            {
                throw new HttpSignatureException("Legacy signature parameter must be quoted.");
            }

            var parsed = new StringBuilder();
            bool closed = false;
            while (position < value.Length)
            {
                char character = value[position++];
                if (character == '"')
                {
                    closed = true;
                    break;
                }

                if (character == '\\')
                {
                    if (position >= value.Length)
                    {
                        throw new HttpSignatureException("Legacy signature contains an incomplete escape.");
                    }

                    character = value[position++];
                }

                if (char.IsControl(character))
                {
                    throw new HttpSignatureException("Legacy signature contains a control character.");
                }

                parsed.Append(character);
            }

            if (!closed || !result.TryAdd(name, parsed.ToString()))
            {
                throw new HttpSignatureException("Legacy signature contains an unterminated or duplicate parameter.");
            }

            SkipWhitespace(value, ref position);
            if (position < value.Length && value[position] != ',')
            {
                throw new HttpSignatureException("Legacy signature parameter separator is malformed.");
            }
        }

        return result;
    }

    private static void SkipWhitespaceAndCommas(string value, ref int position)
    {
        while (position < value.Length && (value[position] == ',' || char.IsWhiteSpace(value[position])))
        {
            position++;
        }
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
        {
            position++;
        }
    }
}
