using System.Security.Cryptography;
using System.Text;

namespace ActivityPub.Federation.Tests;

public sealed class Rfc9421OfficialFixtureTests
{
    // Test vector from RFC 9421 sections 4.3 and B.1.1. RFC code components are
    // provided under the Revised BSD License; see fixtures/RFC9421-LICENSE.txt.
    [Fact]
    public void RsaV15Sha256ProxySignatureVerifiesWithDotNetPrimitive()
    {
        const string publicKey = """
            -----BEGIN RSA PUBLIC KEY-----
            MIIBCgKCAQEAhAKYdtoeoy8zcAcR874L8cnZxKzAGwd7v36APp7Pv6Q2jdsPBRrw
            WEBnez6d0UDKDwGbc6nxfEXAy5mbhgajzrw3MOEt8uA5txSKobBpKDeBLOsdJKFq
            MGmXCQvEG7YemcxDTRPxAleIAgYYRjTSd/QBwVW9OwNFhekro3RtlinV0a75jfZg
            kne/YiktSvLG34lw2zqXBDTC5NHROUqGTlML4PlNZS5Ri2U4aCNx2rUPRcKIlE0P
            uKxI4T+HIaFpv8+rdV6eUgOrB2xeI1dSFFn/nnv5OoZJEIB+VmuKn3DCUcCZSFlQ
            PSXSfBDiUGhwOw76WuSSsf1D4b/vLoJ10wIDAQAB
            -----END RSA PUBLIC KEY-----
            """;
        const string signatureBase = """
            "@method": POST
            "@authority": origin.host.internal.example
            "@path": /foo
            "content-digest": sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:
            "content-type": application/json
            "content-length": 18
            "forwarded": for=192.0.2.123;host=example.com;proto=https
            "@signature-params": ("@method" "@authority" "@path" "content-digest" "content-type" "content-length" "forwarded");created=1618884480;keyid="test-key-rsa";alg="rsa-v1_5-sha256";expires=1618884540
            """;
        const string signatureValue = """
            S6ZzPXSdAMOPjN/6KXfXWNO/f7V6cHm7BXYUh3YD/fRad4BCaRZxP+JH+8XY1I6+8Cy+CM5g92iHgxtRPz+MjniOaYmdkDcnL9cCpXJleXsOckpURl49GwiyUpZ10KHgOEe11sx3G2gxI8S0jnxQB+Pu68U9vVcasqOWAEObtNKKZd8tSFu7LB5YAv0RAGhB8tmpv7sFnIm9y+7X5kXQfi8NMaZaA8i2ZHwpBdg7a6CMfwnnrtflzvZdXAsD3LH2TwevU+/PBPv0B6NMNk93wUs/vfJvye+YuI87HU38lZHowtznbLVdp770I6VHR6WfgS9ddzirrswsE1w5o0LV/g==
            """;

        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        bool verified = rsa.VerifyData(
            Encoding.UTF8.GetBytes(signatureBase),
            Convert.FromBase64String(signatureValue),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        Assert.True(verified);
    }
}
