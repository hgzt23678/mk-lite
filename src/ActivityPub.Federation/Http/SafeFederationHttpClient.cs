using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using ActivityPub.Application;

namespace ActivityPub.Federation.Http;

public sealed record SafeFederationRequest(
    HttpMethod Method,
    Uri Uri,
    byte[]? Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlySet<string> AcceptedMediaTypes,
    int MaximumResponseBytes,
    Func<Uri, CancellationToken, Task<bool>>? TargetValidator = null);

public sealed record SafeFederationResponse(
    HttpStatusCode StatusCode,
    Uri FinalUri,
    string? MediaType,
    byte[] Body,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset? RetryAfter);

public interface ISafeFederationHttpClient
{
    Task<SafeFederationResponse> SendAsync(SafeFederationRequest request, CancellationToken cancellationToken);
}

public interface IFederationDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemFederationDnsResolver : IFederationDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class UnsafeFederationTargetException : Exception
{
    public UnsafeFederationTargetException(string message)
        : base(message)
    {
    }
}

public sealed class FederationTargetPolicyException : Exception
{
    public FederationTargetPolicyException()
        : base("Federation target was rejected by local policy.")
    {
    }
}

public sealed class FederationAddressValidator(
    FederationOptions options,
    IFederationDnsResolver dnsResolver)
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(Uri target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsAbsoluteUri || !string.IsNullOrEmpty(target.UserInfo) || string.IsNullOrWhiteSpace(target.Host))
        {
            throw new UnsafeFederationTargetException("Federation target must be an absolute URL without user information.");
        }

        if (options.RequireHttps && target.Scheme != Uri.UriSchemeHttps)
        {
            throw new UnsafeFederationTargetException("Federation target must use HTTPS.");
        }

        if (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp)
        {
            throw new UnsafeFederationTargetException("Federation target scheme is not permitted.");
        }

        bool explicitlyAllowedDevelopmentHost = options.IsDevelopmentHostAllowed(target.IdnHost);
        if (options.DevelopmentRestrictToAllowedHosts && !explicitlyAllowedDevelopmentHost &&
            !(options.AllowDevelopmentLoopback && IsLoopbackHostName(target.IdnHost)))
        {
            throw new UnsafeFederationTargetException("Federation target is outside the isolated development host allow-list.");
        }

        if (target.Scheme == Uri.UriSchemeHttp && !explicitlyAllowedDevelopmentHost &&
            !(options.AllowDevelopmentLoopback && IsLoopbackHostName(target.IdnHost)))
        {
            throw new UnsafeFederationTargetException(
                "Plain HTTP federation is permitted only for an explicitly allowed development hostname.");
        }

        IPAddress[] addresses = IPAddress.TryParse(target.IdnHost, out IPAddress? literal)
            ? [literal]
            : await dnsResolver.ResolveAsync(target.IdnHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new UnsafeFederationTargetException("Federation target did not resolve to an IP address.");
        }

        var normalized = new List<IPAddress>(addresses.Length);
        foreach (IPAddress address in addresses.Distinct())
        {
            IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (!IsPublic(candidate, options.AllowDevelopmentLoopback) &&
                !(explicitlyAllowedDevelopmentHost && IsDevelopmentPrivateNetwork(candidate)))
            {
                throw new UnsafeFederationTargetException($"Federation DNS result {candidate} is not publicly routable.");
            }

            normalized.Add(candidate);
        }

        return normalized;
    }

    internal static bool IsPublic(IPAddress address, bool allowDevelopmentLoopback)
    {
        if (IPAddress.IsLoopback(address))
        {
            return allowDevelopmentLoopback;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] octets = address.GetAddressBytes();
            return octets[0] switch
            {
                0 or 10 or 127 or >= 224 => false,
                100 when octets[1] is >= 64 and <= 127 => false,
                168 when octets[1] == 63 && octets[2] == 129 && octets[3] == 16 => false,
                169 when octets[1] == 254 => false,
                172 when octets[1] is >= 16 and <= 31 => false,
                192 when octets[1] == 168 => false,
                192 when octets[1] == 0 => false,
                192 when octets[1] == 88 && octets[2] == 99 => false,
                192 when octets[1] == 0 && octets[2] == 2 => false,
                198 when octets[1] is 18 or 19 => false,
                198 when octets[1] == 51 && octets[2] == 100 => false,
                203 when octets[1] == 0 && octets[2] == 113 => false,
                _ => true
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        bool uniqueLocal = (bytes[0] & 0xFE) == 0xFC;
        bool documentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
        return !uniqueLocal && !documentation;
    }

    internal static bool IsDevelopmentPrivateNetwork(IPAddress address)
    {
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (candidate.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] octets = candidate.GetAddressBytes();
            return octets[0] == 10 ||
                octets[0] == 172 && octets[1] is >= 16 and <= 31 ||
                octets[0] == 192 && octets[1] == 168;
        }

        if (candidate.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        byte[] bytes = candidate.GetAddressBytes();
        return (bytes[0] & 0xFE) == 0xFC;
    }

    private static bool IsLoopbackHostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
}

public sealed class SafeFederationHttpClient(
    FederationOptions options,
    FederationAddressValidator addressValidator,
    IFederationInstrumentation instrumentation) : ISafeFederationHttpClient
{
    private const int AbsoluteMaximumResponseBytes = 100 * 1024 * 1024;

    private static readonly HttpStatusCode[] RedirectStatusCodes =
    [
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Redirect,
        HttpStatusCode.RedirectMethod,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect
    ];

    public async Task<SafeFederationResponse> SendAsync(
        SafeFederationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumResponseBytes is < 1 or > AbsoluteMaximumResponseBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Response size limit is outside the supported range.");
        }

        try
        {
            Uri current = request.Uri;
            for (int redirects = 0; ; redirects++)
            {
                if (request.TargetValidator is not null &&
                    !await request.TargetValidator(current, cancellationToken).ConfigureAwait(false))
                {
                    throw new FederationTargetPolicyException();
                }

                IReadOnlyList<IPAddress> addresses = await addressValidator.ResolveAndValidateAsync(current, cancellationToken).ConfigureAwait(false);
                SafeFederationResponse response = await SendHopAsync(request, current, addresses, cancellationToken).ConfigureAwait(false);
                if (!RedirectStatusCodes.Contains(response.StatusCode))
                {
                    return response;
                }

                if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
                {
                    return response;
                }

                if (redirects >= options.MaximumRedirects)
                {
                    throw new HttpRequestException("Federation redirect limit was exceeded.");
                }

                string? location = ExtractRedirectLocation(response.Body);
                if (location is null)
                {
                    throw new HttpRequestException("Federation redirect did not include a Location header.");
                }

                current = new Uri(current, location);
            }
        }
        catch (UnsafeFederationTargetException)
        {
            instrumentation.SsrfRejected();
            throw;
        }
    }

    private async Task<SafeFederationResponse> SendHopAsync(
        SafeFederationRequest request,
        Uri target,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = options.ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.Zero,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = (context, token) => ConnectPinnedAsync(context, addresses, token)
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        using HttpRequestMessage message = CreateMessage(request, target);
        long started = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        instrumentation.RemoteRequest(target.IdnHost, (int)response.StatusCode, Stopwatch.GetElapsedTime(started));

        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > request.MaximumResponseBytes)
        {
            throw new HttpRequestException("Federation response exceeds the configured limit.");
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (response.IsSuccessStatusCode && request.AcceptedMediaTypes.Count > 0 &&
            (mediaType is null || !request.AcceptedMediaTypes.Contains(mediaType)))
        {
            throw new HttpRequestException("Federation response Content-Type is not accepted.");
        }

        byte[] body = await ReadLimitedAsync(response.Content, request.MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
        DateTimeOffset? retryAfter = response.Headers.RetryAfter?.Date;
        if (retryAfter is null && response.Headers.RetryAfter?.Delta is { } delta)
        {
            retryAfter = DateTimeOffset.UtcNow.Add(delta);
        }

        string? redirect = response.Headers.Location?.OriginalString;
        byte[] responseBody = redirect is null ? body : System.Text.Encoding.UTF8.GetBytes(redirect);
        return new(
            response.StatusCode,
            target,
            mediaType,
            responseBody,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            retryAfter);
    }

    private static HttpRequestMessage CreateMessage(SafeFederationRequest request, Uri target)
    {
        var message = new HttpRequestMessage(request.Method, target);
        if (request.Body is not null)
        {
            message.Content = new ByteArrayContent(request.Body);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }
        }

        foreach (KeyValuePair<string, string> header in request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) || header.Key.Contains('\r', StringComparison.Ordinal) ||
                header.Key.Contains('\n', StringComparison.Ordinal) || header.Value.Contains('\r', StringComparison.Ordinal) ||
                header.Value.Contains('\n', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsafe federation request header was rejected.");
            }

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value) &&
                (message.Content is null || !message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value)))
            {
                throw new InvalidOperationException($"Federation request header '{header.Key}' is invalid.");
            }
        }

        foreach (string mediaType in request.AcceptedMediaTypes)
        {
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        }

        return message;
    }

    private static async ValueTask<Stream> ConnectPinnedAsync(
        SocketsHttpConnectionContext context,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastException = exception;
                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException("Unable to connect to a validated federation address.", lastException);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1_024));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1_024);
        try
        {
            int total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new HttpRequestException("Decompressed federation response exceeds the configured limit.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string? ExtractRedirectLocation(byte[] body) =>
        body.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(body);
}
