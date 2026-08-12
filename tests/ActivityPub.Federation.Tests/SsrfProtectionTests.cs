using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Federation.Http;

namespace ActivityPub.Federation.Tests;

public sealed class SsrfProtectionTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task RejectsNonPublicDnsAnswers(string address)
    {
        var validator = new FederationAddressValidator(Options(), new StaticDnsResolver(IPAddress.Parse(address)));

        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("https://remote.example/inbox"), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsWholeResolutionWhenAnyAnswerIsPrivate()
    {
        var validator = new FederationAddressValidator(
            Options(),
            new StaticDnsResolver(IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.5")));

        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("https://remote.example/inbox"), CancellationToken.None));
    }

    [Fact]
    public async Task AcceptsPublicAddressesOnly()
    {
        IPAddress expected = IPAddress.Parse("93.184.216.34");
        var validator = new FederationAddressValidator(Options(), new StaticDnsResolver(expected));

        IReadOnlyList<IPAddress> result = await validator.ResolveAndValidateAsync(
            new Uri("https://remote.example/inbox"),
            CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result));
    }

    [Fact]
    public async Task AcceptsRfc1918OnlyForAnExactDevelopmentHost()
    {
        IPAddress expected = IPAddress.Parse("172.20.0.12");
        var options = DevelopmentOptions(TimeSpan.FromSeconds(2));
        var validator = new FederationAddressValidator(options, new StaticDnsResolver(expected));

        IReadOnlyList<IPAddress> result = await validator.ResolveAndValidateAsync(
            new Uri("http://mastodon/users/alice"),
            CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result));
        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("http://not-mastodon/users/alice"), CancellationToken.None));
    }

    [Fact]
    public async Task IsolatedDevelopmentModeRejectsOtherwisePublicHosts()
    {
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("http://activitypub"),
            RequireHttps = false,
            DevelopmentRestrictToAllowedHosts = true,
            DevelopmentAllowedHosts = ["mastodon"]
        };
        var validator = new FederationAddressValidator(
            options,
            new StaticDnsResolver(IPAddress.Parse("93.184.216.34")));

        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("https://social.example/users/alice"), CancellationToken.None));
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("127.0.0.1")]
    [InlineData("224.0.0.1")]
    public async Task DevelopmentHostNeverAllowsMetadataLoopbackOrSpecialRanges(string address)
    {
        FederationOptions options = WithAllowedHosts(DevelopmentOptions(TimeSpan.FromSeconds(2)), "mastodon");
        var validator = new FederationAddressValidator(options, new StaticDnsResolver(IPAddress.Parse(address)));

        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("http://mastodon/users/alice"), CancellationToken.None));
    }

    [Fact]
    public void ProductionRejectsEveryDevelopmentNetworkException()
    {
        var insecureTransport = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            RequireHttps = false
        };
        var privateHost = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            DevelopmentRestrictToAllowedHosts = true,
            DevelopmentAllowedHosts = ["mastodon"]
        };

        Assert.Throws<InvalidOperationException>(() => insecureTransport.Validate(isProduction: true));
        Assert.Throws<InvalidOperationException>(() => privateHost.Validate(isProduction: true));
    }

    [Fact]
    public void DevelopmentAllowsCanonicalHttpOriginWithExactPrivateHosts()
    {
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("http://activitypub"),
            RequireHttps = false,
            DevelopmentRestrictToAllowedHosts = true,
            DevelopmentAllowedHosts = ["mastodon", "misskey", "pleroma"]
        };

        options.Validate(isProduction: false);
    }

    [Fact]
    public async Task RejectsUserInformationBeforeDns()
    {
        var resolver = new StaticDnsResolver(IPAddress.Parse("93.184.216.34"));
        var validator = new FederationAddressValidator(Options(), resolver);

        await Assert.ThrowsAsync<UnsafeFederationTargetException>(() =>
            validator.ResolveAndValidateAsync(new Uri("https://user:password@remote.example/inbox"), CancellationToken.None));
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task SafeClientAppliesAnOverallTimeoutToAnUnresponsivePeer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = CreateSafeClient(DevelopmentOptions(TimeSpan.FromMilliseconds(150)));
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri($"http://timeout.test:{port}/actor"),
            null,
            null,
            new Dictionary<string, string>(),
            new HashSet<string>(),
            1_024);
        Task<SafeFederationResponse> send = client.SendAsync(request, CancellationToken.None);
        using TcpClient connection = await accept.WaitAsync(TimeSpan.FromSeconds(2));

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task SafeClientAppliesTargetPolicyBeforeDns()
    {
        var resolver = new StaticDnsResolver(IPAddress.Parse("93.184.216.34"));
        FederationOptions options = Options();
        var validator = new FederationAddressValidator(options, resolver);
        var client = new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri("https://policy-blocked.example/avatar.png"),
            null,
            null,
            new Dictionary<string, string>(),
            new HashSet<string> { "image/png" },
            1_024,
            (_, _) => Task.FromResult(false));

        await Assert.ThrowsAsync<FederationTargetPolicyException>(() =>
            client.SendAsync(request, CancellationToken.None));
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task SafeClientReappliesTargetPolicyBeforeFollowingRedirect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = connection.GetStream();
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 302 Found\r\nLocation: http://policy-blocked.test:{port}/avatar.png\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
        });
        var resolver = new StaticDnsResolver(IPAddress.Loopback);
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            RequireHttps = false,
            AllowDevelopmentLoopback = true,
            DevelopmentAllowedHosts = ["policy-allowed.test", "policy-blocked.test"],
            ConnectTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(2)
        };
        var validator = new FederationAddressValidator(options, resolver);
        var client = new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri($"http://policy-allowed.test:{port}/avatar.png"),
            null,
            null,
            new Dictionary<string, string>(),
            new HashSet<string> { "image/png" },
            1_024,
            (target, _) => Task.FromResult(target.IdnHost == "policy-allowed.test"));

        await Assert.ThrowsAsync<FederationTargetPolicyException>(() =>
            client.SendAsync(request, CancellationToken.None));
        await server.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task SafeClientRejectsAResponseThatExceedsTheLimitAfterDecompression()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[] expanded = Encoding.UTF8.GetBytes(new string('a', 8_192));
        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                await gzip.WriteAsync(expanded);
            }

            compressed = buffer.ToArray();
        }

        Task server = Task.Run(async () =>
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = connection.GetStream();
            byte[] headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip\r\nContent-Length: {compressed.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers);
            await stream.WriteAsync(compressed);
        });
        var client = CreateSafeClient(DevelopmentOptions(TimeSpan.FromSeconds(2)));
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri($"http://compressed.test:{port}/actor"),
            null,
            null,
            new Dictionary<string, string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/json" },
            1_024);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendAsync(request, CancellationToken.None));
        await server.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("Decompressed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SafeClientAcceptsConfiguredTenMebibyteMediaLimit()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = connection.GetStream();
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
        });
        var client = CreateSafeClient(DevelopmentOptions(TimeSpan.FromSeconds(2)));
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri($"http://timeout.test:{port}/media.png"),
            null,
            null,
            new Dictionary<string, string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png" },
            10 * 1024 * 1024);

        SafeFederationResponse response = await client.SendAsync(request, CancellationToken.None);
        await server.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Body);
    }

    private static FederationOptions Options() => new()
    {
        PublicBaseUri = new Uri("https://local.example"),
        RequireHttps = true,
        AllowDevelopmentLoopback = false
    };

    private static FederationOptions DevelopmentOptions(TimeSpan requestTimeout) => new()
    {
        PublicBaseUri = new Uri("https://local.example"),
        RequireHttps = false,
        AllowDevelopmentLoopback = true,
        DevelopmentAllowedHosts = ["timeout.test", "compressed.test", "mastodon"],
        ConnectTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = requestTimeout
    };

    private static FederationOptions WithAllowedHosts(FederationOptions options, params string[] hosts) => new()
    {
        PublicBaseUri = options.PublicBaseUri,
        RequireHttps = options.RequireHttps,
        AllowDevelopmentLoopback = false,
        DevelopmentRestrictToAllowedHosts = options.DevelopmentRestrictToAllowedHosts,
        DevelopmentAllowedHosts = hosts,
        ConnectTimeout = options.ConnectTimeout,
        RequestTimeout = options.RequestTimeout
    };

    private static SafeFederationHttpClient CreateSafeClient(FederationOptions options)
    {
        var resolver = new StaticDnsResolver(IPAddress.Loopback);
        var validator = new FederationAddressValidator(options, resolver);
        return new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
    }

    private sealed class StaticDnsResolver(params IPAddress[] addresses) : IFederationDnsResolver
    {
        public int CallCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(addresses);
        }
    }
}
