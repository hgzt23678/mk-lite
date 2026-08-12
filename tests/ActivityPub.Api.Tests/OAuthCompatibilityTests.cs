using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore.Models;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class OAuthCompatibilityTests(ActivityPubApiFixture fixture)
{
    private readonly HttpClient client = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://local.example"),
        AllowAutoRedirect = false
    });

    [Fact]
    public void FrontendAuthUsesTheV12LocalSessionCookieWithoutExternalOidc()
    {
        IOptionsMonitor<CookieAuthenticationOptions> options = fixture.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        CookieAuthenticationOptions cookie = options.Get(OAuthAuthorizationServerExtensions.ExternalSessionScheme);

        Assert.Equal("__Host-activitypub-oauth-session", cookie.Cookie.Name);
        Assert.Equal("/", cookie.Cookie.Path);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);

        var oidcTypes = fixture.Services.GetServices<IConfigureOptions<OpenIdConnectOptions>>();
        Assert.DoesNotContain(oidcTypes, configure => configure.GetType().Name.Contains("OpenIdConnect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoveryPublishesAuthorizationCodePkceAndRevocation()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/.well-known/oauth-authorization-server",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        Assert.Equal("https://local.example/", root.GetProperty("issuer").GetString());
        Assert.Equal("https://local.example/oauth/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://local.example/oauth/token", root.GetProperty("token_endpoint").GetString());
        Assert.Equal("https://local.example/oauth/revoke", root.GetProperty("revocation_endpoint").GetString());
        Assert.Contains("authorization_code", root.GetProperty("grant_types_supported").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("S256", root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task RegistrationClientCredentialsVerificationAndRevocationUsePersistentHashedCredentials()
    {
        string appName = "OAuth fixture " + Guid.NewGuid().ToString("N");
        using HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/api/v1/apps",
            new
            {
                client_name = appName,
                redirect_uris = "https://client.example/callback",
                scopes = "read",
                website = "https://client.example/"
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        using JsonDocument applicationJson = await JsonDocument.ParseAsync(await registration.Content.ReadAsStreamAsync());
        string externalApplicationId = applicationJson.RootElement.GetProperty("id").GetString()!;
        string clientId = applicationJson.RootElement.GetProperty("client_id").GetString()!;
        string clientSecret = applicationJson.RootElement.GetProperty("client_secret").GetString()!;
        Assert.True(long.TryParse(externalApplicationId, out _));
        Assert.True(clientId.Length >= 32);
        Assert.True(clientSecret.Length >= 64);

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            OpenIddictEntityFrameworkCoreApplication stored = await db.Set<OpenIddictEntityFrameworkCoreApplication>()
                .SingleAsync(application => application.ClientId == clientId);
            Assert.NotNull(stored.ClientSecret);
            Assert.NotEqual(clientSecret, stored.ClientSecret);
            Assert.DoesNotContain(clientSecret, stored.ClientSecret!, StringComparison.Ordinal);
            ExternalEntityId mapping = await db.ExternalEntityIds.SingleAsync(item =>
                item.Dialect == ApiDialect.Mastodon &&
                item.EntityType == ExternalEntityType.Application &&
                item.ExternalId == externalApplicationId);
            Assert.Equal(Guid.Parse(stored.Id!), mapping.InternalId);
        }

        using HttpResponseMessage tokenResponse = await RequestClientCredentialsTokenAsync(clientId, clientSecret);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using JsonDocument tokenJson = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()!;
        Assert.Equal("Bearer", tokenJson.RootElement.GetProperty("token_type").GetString(), ignoreCase: true);
        if (tokenJson.RootElement.TryGetProperty("scope", out JsonElement returnedScope))
        {
            Assert.Equal("read", returnedScope.GetString());
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            OpenIddictEntityFrameworkCoreToken storedToken = await db.Set<OpenIddictEntityFrameworkCoreToken>()
                .OrderByDescending(token => token.CreationDate)
                .FirstAsync(token => token.Application!.ClientId == clientId);
            Assert.NotEqual(accessToken, storedToken.ReferenceId);
            Assert.DoesNotContain(accessToken, storedToken.Payload ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit =>
                audit.Category == "oauth" &&
                audit.Action == "application-registered" &&
                audit.Target == clientId);
        }

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/verify_credentials");
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage verification = await client.SendAsync(verifyRequest);
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        using JsonDocument verificationJson = await JsonDocument.ParseAsync(await verification.Content.ReadAsStreamAsync());
        Assert.Equal(externalApplicationId, verificationJson.RootElement.GetProperty("id").GetString());
        Assert.Equal(clientId, verificationJson.RootElement.GetProperty("client_id").GetString());
        Assert.False(verificationJson.RootElement.TryGetProperty("client_secret", out _));

        using var revocation = new HttpRequestMessage(HttpMethod.Post, "/oauth/revoke")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = accessToken,
                ["token_type_hint"] = "access_token"
            })
        };
        revocation.Headers.Authorization = Basic(clientId, clientSecret);
        using HttpResponseMessage revoked = await client.SendAsync(revocation);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using var replay = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/verify_credentials");
        replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage rejected = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsNonLoopbackPlainHttpRedirectUri()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/apps",
            new
            {
                client_name = "insecure redirect fixture",
                redirect_uris = "http://client.example/callback",
                scopes = "read"
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AuthorizationCodePkcePreservesStateAndRotatesRefreshToken()
    {
        const string redirectUri = "https://client.example/oauth/callback";
        using HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/api/v1/apps",
            new
            {
                client_name = "PKCE integration fixture",
                redirect_uris = redirectUri,
                scopes = "read offline_access"
            });
        Assert.True(
            registration.StatusCode == HttpStatusCode.OK,
            registration.StatusCode + ": " + await registration.Content.ReadAsStringAsync());
        using JsonDocument application = await JsonDocument.ParseAsync(await registration.Content.ReadAsStreamAsync());
        string clientId = application.RootElement.GetProperty("client_id").GetString()!;
        string clientSecret = application.RootElement.GetProperty("client_secret").GetString()!;

        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(24));
        string authorizeUri = "/oauth/authorize?response_type=code" +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&scope=" + Uri.EscapeDataString("read offline_access") +
            "&state=" + Uri.EscapeDataString(state) +
            "&code_challenge=" + Uri.EscapeDataString(challenge) +
            "&code_challenge_method=S256";
        string sessionCookie = CreateExternalSessionCookie();

        using var consentRequest = new HttpRequestMessage(HttpMethod.Get, authorizeUri);
        consentRequest.Headers.Add("Cookie", sessionCookie);
        using HttpResponseMessage consent = await client.SendAsync(consentRequest);
        string consentHtml = await consent.Content.ReadAsStringAsync();
        Assert.True(consent.StatusCode == HttpStatusCode.OK, consent.StatusCode + ": " + consentHtml);
        Match csrf = Regex.Match(consentHtml, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(csrf.Success);
        string antiforgeryCookie = RequiredCookie(consent, "__Host-activitypub-oauth-csrf");

        using var approve = new HttpRequestMessage(HttpMethod.Post, authorizeUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(csrf.Groups[1].Value),
                ["decision"] = "approve",
                ["approved_scope"] = "read offline_access",
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = "read offline_access",
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256"
            })
        };
        approve.Headers.Add("Cookie", sessionCookie + "; " + antiforgeryCookie);
        using HttpResponseMessage authorized = await client.SendAsync(approve);
        Assert.True(
            authorized.StatusCode == HttpStatusCode.Redirect,
            authorized.StatusCode + ": " + await authorized.Content.ReadAsStringAsync());
        Uri callback = authorized.Headers.Location!;
        Assert.Equal(new Uri(redirectUri).GetLeftPart(UriPartial.Path), callback.GetLeftPart(UriPartial.Path));
        Dictionary<string, string> callbackQuery = ParseQuery(callback.Query);
        Assert.Equal(state, callbackQuery["state"]);
        string code = callbackQuery["code"];

        using HttpResponseMessage wrongVerifier = await RequestTokenAsync(clientId, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = Base64Url(RandomNumberGenerator.GetBytes(48))
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongVerifier.StatusCode);
        using JsonDocument verifierError = await JsonDocument.ParseAsync(await wrongVerifier.Content.ReadAsStreamAsync());
        Assert.Equal("invalid_grant", verifierError.RootElement.GetProperty("error").GetString());

        using HttpResponseMessage token = await RequestTokenAsync(clientId, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        });
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        using JsonDocument tokenJson = await JsonDocument.ParseAsync(await token.Content.ReadAsStreamAsync());
        string accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()!;
        string refreshToken = tokenJson.RootElement.GetProperty("refresh_token").GetString()!;

        using var verify = new HttpRequestMessage(HttpMethod.Get, "/api/v1/accounts/verify_credentials");
        verify.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage verified = await client.SendAsync(verify);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        using HttpResponseMessage refreshed = await RequestTokenAsync(clientId, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = "read offline_access"
        });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using JsonDocument refreshedJson = await JsonDocument.ParseAsync(await refreshed.Content.ReadAsStreamAsync());
        string rotatedRefreshToken = refreshedJson.RootElement.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        using HttpResponseMessage replay = await RequestTokenAsync(clientId, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using JsonDocument replayJson = await JsonDocument.ParseAsync(await replay.Content.ReadAsStreamAsync());
        Assert.Equal("invalid_grant", replayJson.RootElement.GetProperty("error").GetString());
    }

    private async Task<HttpResponseMessage> RequestClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "read"
            })
        };
        request.Headers.Authorization = Basic(clientId, clientSecret);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(
        string clientId,
        string clientSecret,
        IReadOnlyDictionary<string, string> values)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(values)
        };
        request.Headers.Authorization = Basic(clientId, clientSecret);
        return await client.SendAsync(request);
    }

    private string CreateExternalSessionCookie()
    {
        IOptionsMonitor<CookieAuthenticationOptions> options = fixture.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        CookieAuthenticationOptions cookie = options.Get(OAuthAuthorizationServerExtensions.ExternalSessionScheme);
        Claim[] claims =
        [
            new("sub", "fixture-alice"),
            new("preferred_username", "alice"),
            new(ClaimTypes.Name, "alice")
        ];
        var identity = new ClaimsIdentity(
            claims,
            OAuthAuthorizationServerExtensions.ExternalSessionScheme,
            "preferred_username",
            ClaimTypes.Role);
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20),
            IsPersistent = false
        };
        string value = cookie.TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            OAuthAuthorizationServerExtensions.ExternalSessionScheme));
        return cookie.Cookie.Name + "=" + value;
    }

    private static string RequiredCookie(HttpResponseMessage response, string name)
    {
        string prefix = name + "=";
        string? cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return cookie ?? throw new Xunit.Sdk.XunitException($"Response did not set cookie {name}.");
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.Ordinal);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static AuthenticationHeaderValue Basic(string username, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(username + ":" + password)));
}
