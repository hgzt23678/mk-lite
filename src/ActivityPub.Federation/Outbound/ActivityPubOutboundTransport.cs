using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Protocol;
using ActivityPub.Federation.Signatures;
using Microsoft.AspNetCore.Http;
using NSign.Signatures;

namespace ActivityPub.Federation.Outbound;

public sealed class ActivityPubOutboundTransport(
    ISafeFederationHttpClient httpClient,
    IKeySigner keySigner,
    FederationOptions options,
    IClock clock) : IOutboundTransport
{
    private static readonly IReadOnlySet<string> AcceptedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public async Task<DeliveryTransportResult> DeliverAsync(
        Delivery delivery,
        KeyMaterial key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(key);
        DateTimeOffset started = clock.UtcNow;
        try
        {
            Uri endpoint = new(delivery.EndpointIri);
            IReadOnlyDictionary<string, string> headers = delivery.SignatureProfile switch
            {
                SignatureProfile.LegacyCavage => await CreateLegacyHeadersAsync(endpoint, delivery.Payload, key, cancellationToken).ConfigureAwait(false),
                SignatureProfile.Rfc9421 => await CreateRfc9421HeadersAsync(endpoint, delivery.Payload, key, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Local-client signature profile cannot be used for federation delivery.")
            };
            var request = new SafeFederationRequest(
                HttpMethod.Post,
                endpoint,
                delivery.Payload,
                ActivityStreamsConstants.ActivityJson,
                headers,
                AcceptedMediaTypes,
                options.MaximumRemoteDocumentBytes);
            SafeFederationResponse response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new(
                (int)response.StatusCode,
                clock.UtcNow - started,
                response.RetryAfter,
                null,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, clock.UtcNow - started, null, "http_timeout", "Federation delivery timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or UnsafeFederationTargetException)
        {
            return new(null, clock.UtcNow - started, null, "network_failure", SafeError(exception.Message));
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> CreateLegacyHeadersAsync(
        Uri endpoint,
        byte[] body,
        KeyMaterial key,
        CancellationToken cancellationToken)
    {
        string date = clock.UtcNow.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
        string digest = HttpContentDigestVerifier.CreateLegacy(body);
        string requestTarget = string.IsNullOrEmpty(endpoint.PathAndQuery) ? "/" : endpoint.PathAndQuery;
        string host = endpoint.IsDefaultPort ? endpoint.IdnHost : endpoint.Authority;
        string signingString = string.Join('\n',
            $"(request-target): post {requestTarget}",
            $"host: {host}",
            $"date: {date}",
            $"digest: {digest}",
            $"content-type: {ActivityStreamsConstants.ActivityJson}");
        byte[] signature = await keySigner.SignAsync(
            key.PrivateKeyHandle,
            key.Algorithm,
            Encoding.ASCII.GetBytes(signingString),
            cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Date"] = date,
            ["Digest"] = digest,
            ["Signature"] = $"keyId=\"{EscapeQuoted(key.KeyIri)}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest content-type\",signature=\"{Convert.ToBase64String(signature)}\""
        };
    }

    private async Task<IReadOnlyDictionary<string, string>> CreateRfc9421HeadersAsync(
        Uri endpoint,
        byte[] body,
        KeyMaterial key,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        string contentDigest = HttpContentDigestVerifier.CreateRfc9530(body);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Scheme = endpoint.Scheme;
        httpContext.Request.Host = HostString.FromUriComponent(endpoint);
        httpContext.Request.Path = endpoint.AbsolutePath;
        httpContext.Request.QueryString = new QueryString(endpoint.Query);
        httpContext.Request.Headers.ContentType = ActivityStreamsConstants.ActivityJson;
        httpContext.Request.Headers["Content-Digest"] = contentDigest;

        var parameters = new SignatureParamsComponent()
            .AddComponent(SignatureComponent.Method)
            .AddComponent(SignatureComponent.RequestTargetUri)
            .AddComponent(SignatureComponent.ContentDigest)
            .AddComponent(SignatureComponent.ContentType)
            .WithCreated(now)
            .WithExpires(now.Add(options.SignatureClockSkew))
            .WithKeyId(key.KeyIri)
            .WithAlgorithm(SignatureAlgorithm.RsaPkcs15Sha256);
        var context = new AspNetRequestSignatureContext(httpContext.Request, new NSign.SignatureVerificationOptions());
        ReadOnlyMemory<byte> signatureBase = context.GetSignatureInput(parameters, out string signatureInput);
        byte[] signature = await keySigner.SignAsync(
            key.PrivateKeyHandle,
            key.Algorithm,
            signatureBase,
            cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Date"] = now.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
            ["Digest"] = HttpContentDigestVerifier.CreateLegacy(body),
            ["Content-Digest"] = contentDigest,
            ["Signature-Input"] = $"sig1={signatureInput}",
            ["Signature"] = $"sig1=:{Convert.ToBase64String(signature)}:"
        };
    }

    private static string EscapeQuoted(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string SafeError(string value) => value.Length <= 1_024 ? value : value[..1_024];
}
