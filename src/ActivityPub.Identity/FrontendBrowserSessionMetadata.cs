using Microsoft.AspNetCore.Http;

namespace ActivityPub.Identity;

/// <summary>
/// Marks an endpoint as an explicit first-party browser session boundary.
/// </summary>
/// <remarks>
/// The marker does not authorize a request. Authentication still requires the protected
/// HttpOnly session cookie and unsafe requests additionally require antiforgery validation.
/// Native Misskey clients remain on token authentication unless they opt in with the
/// first-party frontend request header.
/// </remarks>
public sealed class FrontendBrowserSessionMetadata
{
    public const string RequestHeaderName = "X-ActivityPub-Frontend";
    public const string RequestHeaderValue = "1";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    public const string SessionClaim = "activitypub.frontend_session";

    public static FrontendBrowserSessionMetadata Instance { get; } = new();

    private FrontendBrowserSessionMetadata()
    {
    }

    public static bool IsExplicitBrowserRequest(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<FrontendBrowserSessionMetadata>() is not null &&
        string.Equals(
            context.Request.Headers[RequestHeaderName],
            RequestHeaderValue,
            StringComparison.Ordinal);

    public static bool IsBrowserWebSocketRequest(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<FrontendBrowserSessionMetadata>() is not null &&
        context.WebSockets.IsWebSocketRequest &&
        !context.Request.Query.ContainsKey("i");
}
