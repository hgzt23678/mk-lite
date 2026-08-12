using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ActivityPub.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityPub.Identity;

public sealed class MisskeyTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMisskeyAuthenticationService authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const int MaximumJsonRequestBodyBytes = 2_000_000;

    public const string SchemeName = "activitypub.misskey-token";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = await ReadTokenAsync(Request, Context.RequestAborted).ConfigureAwait(false);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        MisskeyTokenPrincipal? validated = await authentication.ValidateAsync(
            token,
            Context.RequestAborted).ConfigureAwait(false);
        if (validated is null)
        {
            return AuthenticateResult.Fail("The Misskey access token is invalid, expired, or revoked.");
        }

        var identity = new ClaimsIdentity(SchemeName, "preferred_username", "role");
        identity.AddClaim(new Claim("sub", validated.TokenId.ToString("N")));
        identity.AddClaim(new Claim("preferred_username", validated.Username));
        identity.AddClaim(new Claim("actor", validated.ActorIri));
        foreach (string permission in validated.Permissions)
        {
            identity.AddClaim(new Claim("misskey.permission", permission));
        }

        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        await Response.WriteAsJsonAsync(new
        {
            error = new
            {
                message = "Authentication is required.",
                code = "AUTHENTICATION_FAILED",
                id = "b0a7f5f8-4976-4c80-b6d7-35d5a00afc2a",
                kind = "client"
            }
        }, Context.RequestAborted).ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        await Response.WriteAsJsonAsync(new
        {
            error = new
            {
                message = "Your app does not have the necessary permissions to use this endpoint.",
                code = "PERMISSION_DENIED",
                id = "1370e5b7-d4eb-4566-bb1d-7748ee6a1838",
                kind = "permission"
            }
        }, Context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<string?> ReadTokenAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        string authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer mk_", StringComparison.Ordinal))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        if (request.Path.StartsWithSegments("/streaming") && request.Query.TryGetValue("i", out var queryToken))
        {
            return queryToken.Count == 1 ? queryToken[0] : null;
        }

        if (!HttpMethods.IsPost(request.Method) ||
            request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true ||
            request.ContentLength is > MaximumJsonRequestBodyBytes)
        {
            return null;
        }

        // The manual reader stops after the first chunk beyond the limit. Do not put the
        // buffering stream itself into a faulted state because the endpoint still has to
        // rewind it and return the canonical 413 response.
        request.EnableBuffering(bufferThreshold: 64 * 1024);
        try
        {
            byte[]? body = await ReadBoundedBodyAsync(request.Body, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(
                body,
                new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("i", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int total = 0;
        int read;
        while ((read = await body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumJsonRequestBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
