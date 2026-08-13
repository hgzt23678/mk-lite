using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;
using IPNetwork = System.Net.IPNetwork;

namespace ActivityPub.Server;

internal sealed class TrustedProxySet
{
    private readonly HashSet<IPAddress> proxies;
    private readonly IReadOnlyList<IPNetwork> networks;

    private TrustedProxySet(HashSet<IPAddress> proxies, IReadOnlyList<IPNetwork> networks)
    {
        this.proxies = proxies;
        this.networks = networks;
    }

    public int Count => proxies.Count + networks.Count;

    public static TrustedProxySet Read(IConfiguration configuration)
    {
        var proxies = new HashSet<IPAddress>();
        foreach (string value in configuration.GetSection("Http:TrustedProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(value, out IPAddress? address))
            {
                throw new InvalidOperationException($"Trusted proxy '{value}' is not an IP address.");
            }

            IPAddress normalized = Normalize(address);
            if (normalized.Equals(IPAddress.Any) || normalized.Equals(IPAddress.IPv6Any))
            {
                throw new InvalidOperationException("An unspecified address cannot be a trusted proxy.");
            }

            _ = proxies.Add(normalized);
        }

        var networks = new List<IPNetwork>();
        foreach (string value in configuration.GetSection("Http:TrustedProxyNetworks").Get<string[]>() ?? [])
        {
            if (!IPNetwork.TryParse(value, out IPNetwork network))
            {
                throw new InvalidOperationException($"Trusted proxy network '{value}' is not an IP CIDR.");
            }

            if (network.PrefixLength == 0)
            {
                throw new InvalidOperationException("A universal network cannot be a trusted proxy network.");
            }

            networks.Add(network);
        }

        return new TrustedProxySet(proxies, networks);
    }

    public void ApplyTo(ForwardedHeadersOptions options)
    {
        // Framework defaults trust loopback. Native proxy configuration is deliberately
        // explicit so a deployment cannot accidentally grow its trust boundary.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (IPAddress proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (IPNetwork network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    public bool Contains(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        IPAddress normalized = Normalize(address);
        return proxies.Contains(normalized) || networks.Any(network => network.Contains(normalized));
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

internal sealed class CloudflareConnectingIpGuard(RequestDelegate next, TrustedProxySet trustedProxies)
{
    internal const string HeaderName = "CF-Connecting-IP";

    public async Task InvokeAsync(HttpContext context)
    {
        bool isTrustedProxy = trustedProxies.Contains(context.Connection.RemoteIpAddress);
        bool hasConnectingIp = context.Request.Headers.TryGetValue(HeaderName, out StringValues values);
        if (!isTrustedProxy)
        {
            if (hasConnectingIp)
            {
                // A direct request may still be needed for local health probes. It must not
                // be allowed to opt into Cloudflare identity by supplying this header.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context).ConfigureAwait(false);
            return;
        }

        if (!hasConnectingIp ||
            values.Count != 1 ||
            !IPAddress.TryParse(values[0], out IPAddress? clientAddress) ||
            !IsUsableClientAddress(clientAddress))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsUsableClientAddress(IPAddress address)
    {
        IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (normalized.Equals(IPAddress.Any) || normalized.Equals(IPAddress.IPv6Any) ||
            IPAddress.IsLoopback(normalized) || normalized.IsIPv6Multicast)
        {
            return false;
        }

        byte[] bytes = normalized.GetAddressBytes();
        return bytes.Length != 4 || bytes[0] is < 224 or > 239;
    }
}
