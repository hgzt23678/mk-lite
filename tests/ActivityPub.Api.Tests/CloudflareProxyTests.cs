using System.Net;
using ActivityPub.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ActivityPub.Api.Tests;

public sealed class CloudflareProxyTests
{
    [Fact]
    public void TrustedProxyConfigurationAcceptsOnlyExplicitAddressesAndNetworks()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Http:TrustedProxies:0"] = "10.20.0.10",
            ["Http:TrustedProxyNetworks:0"] = "192.0.2.0/24"
        });
        TrustedProxySet trusted = TrustedProxySet.Read(configuration);
        var forwarded = new ForwardedHeadersOptions();

        trusted.ApplyTo(forwarded);

        Assert.Equal(2, trusted.Count);
        Assert.True(trusted.Contains(IPAddress.Parse("::ffff:10.20.0.10")));
        Assert.True(trusted.Contains(IPAddress.Parse("192.0.2.41")));
        Assert.False(trusted.Contains(IPAddress.Loopback));
        Assert.Single(forwarded.KnownProxies);
        Assert.Single(forwarded.KnownIPNetworks);
    }

    [Theory]
    [InlineData("Http:TrustedProxies:0", "0.0.0.0")]
    [InlineData("Http:TrustedProxyNetworks:0", "0.0.0.0/0")]
    [InlineData("Http:TrustedProxyNetworks:0", "::/0")]
    public void UniversalTrustedProxyConfigurationIsRejected(string key, string value)
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<InvalidOperationException>(() => TrustedProxySet.Read(configuration));
    }

    [Fact]
    public async Task DirectOriginCallerCannotSpoofCloudflareConnectingIp()
    {
        TrustedProxySet trusted = TrustedProxySet.Read(Configuration(new Dictionary<string, string?>
        {
            ["Http:TrustedProxies:0"] = "10.20.0.10"
        }));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.50");
        context.Request.Headers[CloudflareConnectingIpGuard.HeaderName] = "198.51.100.7";
        bool called = false;
        var guard = new CloudflareConnectingIpGuard(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, trusted);

        await guard.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task DirectHealthProbeWithoutCloudflareHeaderKeepsItsActualPeerAddress()
    {
        TrustedProxySet trusted = TrustedProxySet.Read(Configuration(new Dictionary<string, string?>
        {
            ["Http:TrustedProxies:0"] = "10.20.0.10"
        }));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        bool called = false;
        var guard = new CloudflareConnectingIpGuard(requestContext =>
        {
            called = true;
            Assert.Equal(IPAddress.Loopback, requestContext.Connection.RemoteIpAddress);
            return Task.CompletedTask;
        }, trusted);

        await guard.InvokeAsync(context);

        Assert.True(called);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("ff02::1")]
    [InlineData("198.51.100.7, 198.51.100.8")]
    public async Task TrustedProxyMustSupplyExactlyOneValidConnectingIp(string? header)
    {
        TrustedProxySet trusted = TrustedProxySet.Read(Configuration(new Dictionary<string, string?>
        {
            ["Http:TrustedProxies:0"] = "10.20.0.10"
        }));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.0.10");
        if (header is not null)
        {
            context.Request.Headers[CloudflareConnectingIpGuard.HeaderName] = header;
        }

        var guard = new CloudflareConnectingIpGuard(_ => Task.CompletedTask, trusted);

        await guard.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task TrustedProxyWithCloudflareConnectingIpContinues()
    {
        TrustedProxySet trusted = TrustedProxySet.Read(Configuration(new Dictionary<string, string?>
        {
            ["Http:TrustedProxyNetworks:0"] = "10.20.0.0/24"
        }));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.0.10");
        context.Request.Headers[CloudflareConnectingIpGuard.HeaderName] = "2001:db8::7";
        bool called = false;
        var guard = new CloudflareConnectingIpGuard(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, trusted);

        await guard.InvokeAsync(context);

        Assert.True(called);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
