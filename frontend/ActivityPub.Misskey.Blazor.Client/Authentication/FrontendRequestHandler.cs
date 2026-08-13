using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class FrontendRequestHandler(
    FrontendAntiforgeryTokenStore antiforgeryTokens,
    FrontendOrigin frontendOrigin) : DelegatingHandler
{
    public const string FrontendHeaderName = "X-ActivityPub-Frontend";
    private static readonly HashSet<HttpMethod> SafeMethods =
    [
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Trace
    ];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri requestUri = request.RequestUri is { IsAbsoluteUri: true } absoluteRequestUri
            ? absoluteRequestUri
            : new Uri(frontendOrigin.Value, request.RequestUri
                ?? throw new InvalidOperationException("A first-party request must have a request URI."));
        if (!IsSameOrigin(requestUri) || !IsFirstPartyApiPath(requestUri.AbsolutePath))
        {
            return base.SendAsync(request, cancellationToken);
        }

        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.Remove(FrontendHeaderName);
        request.Headers.TryAddWithoutValidation(FrontendHeaderName, "1");
        request.Headers.CacheControl ??= new CacheControlHeaderValue { NoStore = true };

        bool usesBearer = request.Headers.Authorization is not null;
        if (!SafeMethods.Contains(request.Method) && !usesBearer)
        {
            string token = antiforgeryTokens.RequestToken
                ?? throw new InvalidOperationException(
                    "The browser session bootstrap must complete before a state-changing request is sent.");
            request.Headers.Remove(FrontendAntiforgeryTokenStore.RequiredHeaderName);
            request.Headers.TryAddWithoutValidation(
                FrontendAntiforgeryTokenStore.RequiredHeaderName,
                token);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool IsSameOrigin(Uri requestUri) =>
        requestUri.UserInfo.Length == 0 &&
        string.Equals(requestUri.Scheme, frontendOrigin.Value.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(requestUri.IdnHost, frontendOrigin.Value.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        requestUri.Port == frontendOrigin.Value.Port;

    private static bool IsFirstPartyApiPath(string path) =>
        path.Equals("/api", StringComparison.Ordinal) ||
        path.StartsWith("/api/", StringComparison.Ordinal) ||
        path.Equals("/auth", StringComparison.Ordinal) ||
        path.StartsWith("/auth/", StringComparison.Ordinal);
}

public sealed record FrontendOrigin(Uri Value);
