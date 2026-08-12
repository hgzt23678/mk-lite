using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Outbound;
using ActivityPub.Federation.Signatures;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.Federation.Tests;

public sealed class LegacySignatureFixtureTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task VerifiesExactRequestTargetHostDateAndDigestFixture()
    {
        using RSA rsa = RSA.Create(2_048);
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var resolver = new FixtureKeyResolver(publicKey);
        var verifier = new HttpSignatureVerifier(resolver, NullFederationInstrumentation.Instance, Options(), new FixedClock());
        byte[] body = Encoding.UTF8.GetBytes("{\"type\":\"Follow\"}");
        DefaultHttpContext context = CreateContext(body);
        string signingString = $"(request-target): post /users/bob/inbox\nhost: local.example\ndate: {Now:r}\ndigest: {HttpContentDigestVerifier.CreateLegacy(body)}";
        byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        context.Request.Headers["Signature"] = $"keyId=\"https://remote.example/users/alice#main-key\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest\",signature=\"{Convert.ToBase64String(signature)}\"";

        HttpSignatureVerification result = await verifier.VerifyAsync(context, body, CancellationToken.None);

        Assert.Equal(SignatureProfile.LegacyCavage, result.Profile);
        Assert.Equal("https://remote.example/users/alice", result.KeyOwnerIri);
        Assert.Equal(1, resolver.ResolveCount);
    }

    [Fact]
    public async Task InvalidSignatureRefreshesKeyExactlyOnce()
    {
        using RSA rsa = RSA.Create(2_048);
        var resolver = new FixtureKeyResolver(rsa.ExportSubjectPublicKeyInfoPem());
        var verifier = new HttpSignatureVerifier(resolver, NullFederationInstrumentation.Instance, Options(), new FixedClock());
        byte[] body = Encoding.UTF8.GetBytes("{\"type\":\"Follow\"}");
        DefaultHttpContext context = CreateContext(body);
        context.Request.Headers["Signature"] = $"keyId=\"https://remote.example/users/alice#main-key\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest\",signature=\"{Convert.ToBase64String(new byte[256])}\"";

        await Assert.ThrowsAsync<HttpSignatureException>(() => verifier.VerifyAsync(context, body, CancellationToken.None));

        Assert.Equal(2, resolver.ResolveCount);
        Assert.True(resolver.LastForceRefresh);
    }

    [Fact]
    public void DigestVerificationRejectsModifiedBytes()
    {
        byte[] original = Encoding.UTF8.GetBytes("original");
        byte[] modified = Encoding.UTF8.GetBytes("modified");
        var context = new DefaultHttpContext();
        context.Request.Headers["Digest"] = HttpContentDigestVerifier.CreateLegacy(original);

        Assert.Throws<HttpSignatureException>(() => HttpContentDigestVerifier.VerifyRequired(context.Request, modified));
    }

    [Fact]
    public async Task Rfc9421OutboundHeadersRoundTripThroughInboundVerifier()
    {
        using RSA rsa = RSA.Create(2_048);
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var resolver = new FixtureKeyResolver(publicKey);
        var remote = new CapturingFederationClient();
        var transport = new ActivityPubOutboundTransport(remote, new FixtureSigner(rsa), Options(), new FixedClock());
        byte[] body = Encoding.UTF8.GetBytes("{\"id\":\"https://local.example/activities/1\",\"type\":\"Create\"}");
        Delivery delivery = Delivery.Create(
            Guid.NewGuid(),
            "https://local.example/activities/1",
            "https://remote.example/inbox?tenant=one",
            "https://local.example/users/alice",
            body,
            SignatureProfile.Rfc9421,
            Now);
        var key = new KeyMaterial(
            "https://remote.example/users/alice#main-key",
            "https://remote.example/users/alice",
            publicKey,
            "fixture",
            "rsa-v1_5-sha256");

        DeliveryTransportResult sent = await transport.DeliverAsync(delivery, key, CancellationToken.None);
        Assert.Equal(202, sent.StatusCode);
        SafeFederationRequest captured = Assert.IsType<SafeFederationRequest>(remote.Request);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = captured.Uri.Scheme;
        context.Request.Host = HostString.FromUriComponent(captured.Uri);
        context.Request.Path = captured.Uri.AbsolutePath;
        context.Request.QueryString = new QueryString(captured.Uri.Query);
        context.Request.ContentType = captured.ContentType;
        foreach (KeyValuePair<string, string> header in captured.Headers)
        {
            context.Request.Headers[header.Key] = header.Value;
        }

        var verifier = new HttpSignatureVerifier(resolver, NullFederationInstrumentation.Instance, Options(), new FixedClock());
        HttpSignatureVerification verification = await verifier.VerifyAsync(context, body, CancellationToken.None);

        Assert.Equal(SignatureProfile.Rfc9421, verification.Profile);
        Assert.Equal(key.KeyIri, verification.KeyIri);
        Assert.Equal(1, resolver.ResolveCount);
    }

    [Fact]
    public async Task DnsSocketFailureIsRecordedAsRetryableTransportResult()
    {
        using RSA rsa = RSA.Create(2_048);
        var transport = new ActivityPubOutboundTransport(
            new SocketFailureFederationClient(),
            new FixtureSigner(rsa),
            Options(),
            new FixedClock());
        Delivery delivery = Delivery.Create(
            Guid.NewGuid(),
            "https://local.example/activities/socket-failure",
            "https://remote.example/inbox",
            "https://local.example/users/alice",
            "{}"u8.ToArray(),
            SignatureProfile.LegacyCavage,
            Now);
        var key = new KeyMaterial(
            "https://local.example/users/alice#main-key",
            "https://local.example/users/alice",
            rsa.ExportSubjectPublicKeyInfoPem(),
            "fixture",
            "rsa-v1_5-sha256");

        DeliveryTransportResult result = await transport.DeliverAsync(delivery, key, CancellationToken.None);

        Assert.Null(result.StatusCode);
        Assert.Equal("network_failure", result.ErrorCode);
    }

    private static DefaultHttpContext CreateContext(byte[] body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("local.example");
        context.Request.Path = "/users/bob/inbox";
        context.Request.Headers.Date = Now.ToString("r", CultureInfo.InvariantCulture);
        context.Request.Headers["Digest"] = HttpContentDigestVerifier.CreateLegacy(body);
        return context;
    }

    private static FederationOptions Options() => new()
    {
        PublicBaseUri = new Uri("https://local.example"),
        SignatureClockSkew = TimeSpan.FromMinutes(5)
    };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixtureKeyResolver(string publicKey) : IRemoteKeyResolver
    {
        public int ResolveCount { get; private set; }
        public bool LastForceRefresh { get; private set; }

        public Task<RemotePublicKey> ResolveAsync(string keyIri, bool forceRefresh, CancellationToken cancellationToken)
        {
            ResolveCount++;
            LastForceRefresh = forceRefresh;
            return Task.FromResult(new RemotePublicKey(
                keyIri,
                "https://remote.example/users/alice",
                publicKey,
                "rsa-v1_5-sha256",
                Now.AddHours(1)));
        }
    }

    private sealed class FixtureSigner(RSA rsa) : IKeySigner
    {
        public Task<byte[]> SignAsync(
            string privateKeyHandle,
            string algorithm,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken) =>
            Task.FromResult(rsa.SignData(data.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private sealed class CapturingFederationClient : ISafeFederationHttpClient
    {
        public SafeFederationRequest? Request { get; private set; }

        public Task<SafeFederationResponse> SendAsync(SafeFederationRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new SafeFederationResponse(
                HttpStatusCode.Accepted,
                request.Uri,
                null,
                [],
                null,
                null,
                null));
        }
    }

    private sealed class SocketFailureFederationClient : ISafeFederationHttpClient
    {
        public Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<SafeFederationResponse>(new SocketException((int)SocketError.HostNotFound));
    }
}
