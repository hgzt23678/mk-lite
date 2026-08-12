using System.Net;
using System.Net.Sockets;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Outbound;

namespace ActivityPub.Federation.Tests;

public sealed class UrlPreviewFetcherTests
{
    private sealed class StaticDnsResolver(params IPAddress[] addresses) : IFederationDnsResolver
    {
        public int CallCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            CallCount += 1;
            return Task.FromResult(addresses);
        }
    }

    [Fact]
    public async Task FetcherParsesThePinnedOgpContractIncludingVideoPlayer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string html =
            "<!DOCTYPE html><html><head>" +
            "<title>Fallback Title</title>" +
            "<meta property=\"og:title\" content=\"OGP Title\">" +
            "<meta property=\"og:description\" content=\"An article description\">" +
            "<meta property=\"og:image\" content=\"https://images.example/cover.png\">" +
            "<meta property=\"og:site_name\" content=\"Example Site\">" +
            "<meta property=\"og:video:url\" content=\"https://player.example/clip.mp4\">" +
            "<meta property=\"og:video:width\" content=\"640\">" +
            "<meta property=\"og:video:height\" content=\"360\">" +
            "<link rel=\"icon\" href=\"/favicon.ico\">" +
            "</head><body></body></html>";
        Task server = Task.Run(async () =>
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = connection.GetStream();
            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.WriteAsync(body);
        });
        StaticDnsResolver resolver = new(IPAddress.Loopback);
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            RequireHttps = false,
            AllowDevelopmentLoopback = true,
            DevelopmentAllowedHosts = ["preview.test"],
            ConnectTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(2)
        };
        var validator = new FederationAddressValidator(options, resolver);
        var client = new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
        var fetcher = new UrlPreviewFetcher(client);

        UrlPreviewResult? result = await fetcher.FetchAsync(
            $"http://preview.test:{port}/article",
            "ja-JP",
            CancellationToken.None);
        await server.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(result);
        Assert.Equal("OGP Title", result.Title);
        Assert.Equal("An article description", result.Description);
        Assert.Equal("https://images.example/cover.png", result.Thumbnail);
        Assert.Equal($"http://preview.test:{port}/favicon.ico", result.Icon);
        Assert.Equal("Example Site", result.SiteName);
        Assert.Equal("https://player.example/clip.mp4", result.PlayerUrl);
        Assert.Equal(640, result.PlayerWidth);
        Assert.Equal(360, result.PlayerHeight);
    }

    [Fact]
    public void HtmlMetaParserResolvesRelativeIconsAgainstTheBaseUrl()
    {
        string html =
            "<!DOCTYPE html><html><head>" +
            "<meta property=\"og:title\" content=\"OGP Title\">" +
            "<link rel=\"icon\" href=\"/favicon.ico\">" +
            "</head></html>";
        HtmlMetaSummary summary = HtmlMetaParser.Parse(html, "http://preview.test:1234/article");
        Assert.Equal("OGP Title", summary.Title);
        Assert.Equal("http://preview.test:1234/favicon.ico", summary.Icon);
    }

    [Fact]
    public async Task FetcherFallsBackToTheHtmlTitleWhenOgpIsAbsent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string html = "<html><head><title>Plain Title</title></head><body></body></html>";
        Task server = Task.Run(async () =>
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = connection.GetStream();
            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.WriteAsync(body);
        });
        StaticDnsResolver resolver = new(IPAddress.Loopback);
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            RequireHttps = false,
            AllowDevelopmentLoopback = true,
            DevelopmentAllowedHosts = ["preview.test"],
            ConnectTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(2)
        };
        var validator = new FederationAddressValidator(options, resolver);
        var client = new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
        var fetcher = new UrlPreviewFetcher(client);

        UrlPreviewResult? result = await fetcher.FetchAsync(
            $"http://preview.test:{port}/page",
            null,
            CancellationToken.None);
        await server.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(result);
        Assert.Equal("Plain Title", result.Title);
        Assert.Null(result.PlayerUrl);
    }

    [Fact]
    public async Task FetcherReturnsNullForPrivateNetworkTargets()
    {
        StaticDnsResolver resolver = new(IPAddress.Parse("10.0.0.5"));
        var options = new FederationOptions
        {
            PublicBaseUri = new Uri("https://local.example"),
            RequireHttps = false,
            AllowDevelopmentLoopback = false,
            ConnectTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(2)
        };
        var validator = new FederationAddressValidator(options, resolver);
        var client = new SafeFederationHttpClient(options, validator, NullFederationInstrumentation.Instance);
        var fetcher = new UrlPreviewFetcher(client);

        UrlPreviewResult? result = await fetcher.FetchAsync(
            "http://internal.example/article",
            null,
            CancellationToken.None);

        Assert.Null(result);
    }
}
