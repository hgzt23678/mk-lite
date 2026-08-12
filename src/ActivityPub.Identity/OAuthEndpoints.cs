using System.Security.Claims;
using System.Text.Encodings.Web;
using ActivityPub.Application;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActivityPub.Identity;

public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapActivityPubOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/oauth/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync)
            .RequireRateLimiting("local-api");
        endpoints.MapPost("/oauth/token", (Delegate)TokenAsync).RequireRateLimiting("local-api");
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IFederationQueryStore actors,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OAuth authorization request is unavailable.");
        AuthenticateResult session = await context.AuthenticateAsync(
            OAuthAuthorizationServerExtensions.ExternalSessionScheme).ConfigureAwait(false);
        if (!session.Succeeded || session.Principal is null)
        {
            return Results.Json(
                new { error = "login_required", error_description = "The user session is required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        string? subject = session.Principal.FindFirst(Claims.Subject)?.Value ??
            session.Principal.FindFirst("sub")?.Value;
        string? username = session.Principal.FindFirst(Claims.PreferredUsername)?.Value ??
            session.Principal.FindFirst("preferred_username")?.Value ??
            session.Principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username) ||
            await actors.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false) is null)
        {
            return Results.Forbid();
        }

        if (HttpMethods.IsGet(context.Request.Method))
        {
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(RenderConsentPage(context, request, tokens, username), "text/html; charset=utf-8");
        }

        await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(form["decision"], "approve", StringComparison.Ordinal))
        {
            await audit.AppendAsync(
                "oauth",
                "consent-denied",
                subject,
                request.ClientId ?? "unknown-client",
                "{}",
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        string[] requestedScopes = request.GetScopes().ToArray();
        string[] approvedScopes = form["approved_scope"].SelectMany(value =>
                value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(scope => requestedScopes.Contains(scope, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ClaimsPrincipal principal = CreatePrincipal(subject, username, approvedScopes, request.ClientId);
        await audit.AppendAsync(
            "oauth",
            "consent-approved",
            subject,
            request.ClientId ?? "unknown-client",
            System.Text.Json.JsonSerializer.Serialize(new { scopes = approvedScopes }),
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext context)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OAuth token request is unavailable.");
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            AuthenticateResult result = await context.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
            if (!result.Succeeded || result.Principal is null)
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            if (request.IsRefreshTokenGrantType() && request.GetScopes().Any())
            {
                string[] narrowed = request.GetScopes()
                    .Where(scope => result.Principal.HasScope(scope))
                    .ToArray();
                if (narrowed.Length != request.GetScopes().Length)
                {
                    return Results.BadRequest(new { error = Errors.InvalidScope });
                }

                result.Principal.SetScopes(narrowed);
            }

            SetClaimDestinations(result.Principal);
            return Results.SignIn(result.Principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            string clientId = request.ClientId ?? throw new InvalidOperationException("A validated client identifier is required.");
            ClaimsPrincipal principal = CreatePrincipal(clientId, clientId, request.GetScopes(), clientId);
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The OAuth grant type was not enabled.");
    }

    private static ClaimsPrincipal CreatePrincipal(
        string subject,
        string name,
        IEnumerable<string> scopes,
        string? presenter)
    {
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);
        identity.AddClaim(new Claim(Claims.Subject, subject));
        identity.AddClaim(new Claim(Claims.Name, name));
        identity.AddClaim(new Claim(Claims.PreferredUsername, name));
        ClaimsPrincipal principal = new(identity);
        principal.SetScopes(scopes);
        principal.SetResources("activitypub-api");
        if (!string.IsNullOrWhiteSpace(presenter)) principal.SetPresenters(presenter);
        SetClaimDestinations(principal);
        return principal;
    }

    private static void SetClaimDestinations(ClaimsPrincipal principal) =>
        principal.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject or Claims.Name or Claims.PreferredUsername or "scope" => [Destinations.AccessToken],
            _ => []
        });

    private static string RenderConsentPage(
        HttpContext context,
        OpenIddictRequest request,
        AntiforgeryTokenSet tokens,
        string username)
    {
        string action = HtmlEncoder.Default.Encode(context.Request.PathBase + context.Request.Path);
        string client = HtmlEncoder.Default.Encode(request.ClientId ?? "unknown-client");
        string encodedUsername = HtmlEncoder.Default.Encode(username);
        string csrfName = HtmlEncoder.Default.Encode(tokens.FormFieldName);
        string csrfValue = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
        string scopes = string.Join(Environment.NewLine, request.GetScopes().Select(scope =>
            $"<label><input type=\"checkbox\" name=\"approved_scope\" value=\"{HtmlEncoder.Default.Encode(scope)}\" checked> {HtmlEncoder.Default.Encode(scope)}</label>"));
        string protocolParameters = string.Concat(
            Hidden("response_type", request.ResponseType),
            Hidden("client_id", request.ClientId),
            Hidden("redirect_uri", request.RedirectUri),
            Hidden("scope", request.Scope),
            Hidden("state", request.State),
            Hidden("code_challenge", request.CodeChallenge),
            Hidden("code_challenge_method", request.CodeChallengeMethod),
            Hidden("response_mode", request.ResponseMode),
            Hidden("nonce", request.Nonce));
        return $"""
            <!doctype html>
            <html lang="ja">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>アプリ連携の確認</title></head>
            <body>
              <main>
                <h1>アプリ連携の確認</h1>
                <p>ログイン中のアカウント: {encodedUsername}</p>
                <p>クライアント: {client}</p>
                <form method="post" action="{action}">
                  <input type="hidden" name="{csrfName}" value="{csrfValue}">
                  {protocolParameters}
                  <fieldset><legend>要求された権限</legend>{scopes}</fieldset>
                  <button type="submit" name="decision" value="approve">許可</button>
                  <button type="submit" name="decision" value="deny">拒否</button>
                </form>
              </main>
            </body>
            </html>
            """;
    }

    private static string Hidden(string name, string? value) => string.IsNullOrEmpty(value)
        ? string.Empty
        : $"<input type=\"hidden\" name=\"{HtmlEncoder.Default.Encode(name)}\" value=\"{HtmlEncoder.Default.Encode(value)}\">";
}
