using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class PublicEndpointTests(ActivityPubApiFixture fixture)
{
    private readonly HttpClient client = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://local.example"),
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task WebFingerReturnsMastodonCompatibleSelfLink()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/.well-known/webfinger?resource=acct%3Aalice%40local.example",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/jrd+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("acct:alice@local.example", json.RootElement.GetProperty("subject").GetString());
        JsonElement self = Assert.Single(json.RootElement.GetProperty("links").EnumerateArray(),
            link => link.GetProperty("rel").GetString() == "self");
        Assert.Equal("https://local.example/users/alice", self.GetProperty("href").GetString());
    }

    [Fact]
    public async Task MastodonApiPublishesInstanceAndAccountCompatibilityShapes()
    {
        using HttpResponseMessage instanceResponse = await client.GetAsync("/api/v2/instance", CancellationToken.None);
        using HttpResponseMessage lookupResponse = await client.GetAsync("/api/v1/accounts/lookup?acct=alice", CancellationToken.None);
        using HttpResponseMessage accountResponse = await client.GetAsync(
            $"/api/v1/accounts/{fixture.MastodonLocalActorId}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, instanceResponse.StatusCode);
        using JsonDocument instance = await JsonDocument.ParseAsync(await instanceResponse.Content.ReadAsStreamAsync());
        Assert.Equal("local.example", instance.RootElement.GetProperty("domain").GetString());
        Assert.True(instance.RootElement.TryGetProperty("configuration", out _));
        Assert.Equal(1, instance.RootElement.GetProperty("usage").GetProperty("users").GetProperty("active_month").GetInt64());

        using HttpResponseMessage instanceV1Response = await client.GetAsync("/api/v1/instance", CancellationToken.None);
        using JsonDocument instanceV1 = await JsonDocument.ParseAsync(await instanceV1Response.Content.ReadAsStreamAsync());
        Assert.Equal(1, instanceV1.RootElement.GetProperty("stats").GetProperty("user_count").GetInt64());
        Assert.True(instanceV1.RootElement.GetProperty("stats").GetProperty("status_count").GetInt64() >= 1);
        Assert.True(instanceV1.RootElement.GetProperty("stats").GetProperty("domain_count").GetInt64() >= 2);

        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        using JsonDocument lookup = await JsonDocument.ParseAsync(await lookupResponse.Content.ReadAsStreamAsync());
        Assert.Equal(fixture.MastodonLocalActorId, lookup.RootElement.GetProperty("id").GetString());
        Assert.Equal("alice", lookup.RootElement.GetProperty("acct").GetString());

        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        using JsonDocument account = await JsonDocument.ParseAsync(await accountResponse.Content.ReadAsStreamAsync());
        Assert.Equal("https://local.example/users/alice", account.RootElement.GetProperty("uri").GetString());
    }

    [Fact]
    public async Task MisskeyV12MetaTimelineAndReactionShapesAreAvailable()
    {
        using HttpResponseMessage metaResponse = await client.PostAsJsonAsync("/api/meta", new { }, CancellationToken.None);
        using HttpResponseMessage timelineResponse = await client.PostAsJsonAsync(
            "/api/notes/global-timeline",
            new { limit = 20 },
            CancellationToken.None);
        using HttpResponseMessage noteResponse = await client.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId = fixture.MisskeyPublicPostId },
            CancellationToken.None);
        using HttpResponseMessage reactionsResponse = await client.PostAsJsonAsync(
            "/api/notes/reactions",
            new { noteId = fixture.MisskeyPublicPostId, limit = 20 },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);
        using JsonDocument meta = await JsonDocument.ParseAsync(await metaResponse.Content.ReadAsStreamAsync());
        Assert.Equal("12.119.2-activitypub-dotnet", meta.RootElement.GetProperty("version").GetString());
        Assert.True(meta.RootElement.GetProperty("enableServiceWorker").GetBoolean());

        using HttpResponseMessage statsResponse = await client.PostAsJsonAsync("/api/stats", new { }, CancellationToken.None);
        using JsonDocument stats = await JsonDocument.ParseAsync(await statsResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, stats.RootElement.GetProperty("usersCount").GetInt64());
        Assert.True(stats.RootElement.GetProperty("instances").GetInt64() >= 2);
        Assert.True(stats.RootElement.GetProperty("driveUsageLocal").GetInt64() >= 1_024);

        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        string timelineBody = await timelineResponse.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain("private-fixture-secret", timelineBody, StringComparison.Ordinal);
        Assert.DoesNotContain("silenced-public-secret", timelineBody, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, noteResponse.StatusCode);
        using JsonDocument note = await JsonDocument.ParseAsync(await noteResponse.Content.ReadAsStreamAsync());
        Assert.Equal(":party@silenced.example:", Assert.Single(
            note.RootElement.GetProperty("reactions").EnumerateObject()).Name);
        Assert.Equal(1, note.RootElement.GetProperty("reactions").GetProperty(":party@silenced.example:").GetInt64());
        string emojiProxy = note.RootElement.GetProperty("emojis").GetProperty("party@silenced.example").GetString()!;
        Assert.StartsWith($"/media/proxy/{fixture.PublicMediaObjectId:N}/", emojiProxy, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.silenced.example", await noteResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, reactionsResponse.StatusCode);
        using JsonDocument reactions = await JsonDocument.ParseAsync(await reactionsResponse.Content.ReadAsStreamAsync());
        Assert.Equal(":party@silenced.example:", Assert.Single(reactions.RootElement.EnumerateArray()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task MisskeyFederationInstancesProjectsDurableRemoteState()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/federation/instances",
            new { sort = "+pubSub", limit = 20 },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement instance = Assert.Single(
            document.RootElement.EnumerateArray(),
            value => value.GetProperty("host").GetString() == "media-blocked.example");
        Assert.Equal(10, instance.GetProperty("id").GetString()!.Length);
        Assert.Equal(1, instance.GetProperty("usersCount").GetInt64());
        Assert.True(instance.GetProperty("notesCount").GetInt64() >= 1);
        Assert.False(instance.GetProperty("isBlocked").GetBoolean());
        Assert.False(instance.GetProperty("isSuspended").GetBoolean());
        Assert.True(instance.TryGetProperty("latestRequestSentAt", out _));
        Assert.True(instance.TryGetProperty("softwareName", out JsonElement software));
        Assert.Equal(JsonValueKind.Null, software.ValueKind);

        using HttpResponseMessage filtered = await client.PostAsJsonAsync(
            "/api/federation/instances",
            new { host = "media-blocked", limit = 1 },
            CancellationToken.None);
        using JsonDocument filteredDocument = await JsonDocument.ParseAsync(await filtered.Content.ReadAsStreamAsync());
        JsonElement filteredInstance = Assert.Single(filteredDocument.RootElement.EnumerateArray());
        Assert.Equal(instance.GetProperty("id").GetString(), filteredInstance.GetProperty("id").GetString());

        using HttpResponseMessage invalid = await client.PostAsJsonAsync(
            "/api/federation/instances",
            new { limit = 101 },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using JsonDocument invalidDocument = await JsonDocument.ParseAsync(await invalid.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PARAM", invalidDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MisskeyPublicTimelinesDoNotProjectUnlistedObjects()
    {
        string marker = "unlisted-timeline-secret-" + Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FederatedObject item = FederatedObject.Create(
            $"https://local.example/objects/{Guid.NewGuid():N}",
            "https://local.example/users/alice",
            "Note",
            Visibility.Unlisted,
            JsonSerializer.Serialize(new
            {
                type = "Note",
                attributedTo = "https://local.example/users/alice",
                content = marker
            }),
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(marker)),
            now,
            now);
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            db.Objects.Add(item);
            await db.SaveChangesAsync();
            IExternalEntityIdService ids = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
            _ = await ids.GetOrCreateAsync(ApiDialect.Misskey, ExternalEntityType.Post, item.Id, now, CancellationToken.None);
        }

        using HttpResponseMessage global = await client.PostAsJsonAsync(
            "/api/notes/global-timeline",
            new { limit = 40 },
            CancellationToken.None);
        using HttpResponseMessage local = await client.PostAsJsonAsync(
            "/api/notes/local-timeline",
            new { limit = 40 },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, global.StatusCode);
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        Assert.DoesNotContain(marker, await global.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(marker, await local.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/users/notes")]
    [InlineData("/api/notes/reactions")]
    public async Task MisskeyRequiredIdentifierCannotMasqueradeAsAnEmptyCollection(string path)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PARAM", error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("3d81ceae-475f-4600-b2a8-2bc116157532", error.RootElement.GetProperty("error").GetProperty("id").GetString());
    }

    [Fact]
    public async Task MisskeyUserNotesReturnsNoSuchUserInsteadOfAnEmptySuccessForUnknownUser()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/notes",
            new { userId = "9zzzzzzzzz" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("NO_SUCH_USER", error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("27e494ba-2ac2-48e8-893b-10d4d8c2387b", error.RootElement.GetProperty("error").GetProperty("id").GetString());
    }

    [Fact]
    public async Task MisskeyNoteShowDoesNotLeakMentionedOnlyObject()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId = fixture.MisskeyPrivatePostId },
            CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("private-fixture-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendRuntimeConfigurationContainsOnlyPublicOidcMetadata()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/frontend/config", CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("activitypub-web-test", json.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("https://client.local.example", json.RootElement.GetProperty("publicBaseUri").GetString());
        Assert.Equal("https://client.local.example/api/", json.RootElement.GetProperty("apiBaseUri").GetString());
        Assert.Equal("https://client.local.example/oidc/realms/test", json.RootElement.GetProperty("authority").GetString());
        Assert.Equal("https://client.local.example/auth/callback", json.RootElement.GetProperty("redirectUri").GetString());
        Assert.Equal("https://client.local.example/", json.RootElement.GetProperty("postLogoutRedirectUri").GetString());
        Assert.Equal("https://source.local.example/activitypub-web", json.RootElement.GetProperty("sourceUrl").GetString());
        Assert.False(json.RootElement.GetProperty("allowInsecureDevelopmentOidc").GetBoolean());
        Assert.True(json.RootElement.GetProperty("capabilities").GetProperty("renote").GetBoolean());
        Assert.True(json.RootElement.GetProperty("capabilities").GetProperty("streaming").GetBoolean());
    }

    [Fact]
    public async Task FrontendResponsesApplyRestrictiveBrowserSecurityPolicy()
    {
        using HttpResponseMessage response = await client.GetAsync("/not-built-in-test-host", CancellationToken.None);

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out IEnumerable<string>? values));
        string policy = Assert.Single(values);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' data:", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://client.local.example", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self' 'nonce-", policy, StringComparison.Ordinal);
        Assert.Contains("style-src-elem 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src-attr 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendRootIsServerRenderedWithoutWebAssemblyBootRuntime()
    {
        using HttpResponseMessage response = await client.GetAsync("/", CancellationToken.None);
        string html = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("class=\"mk-app\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"rsqzvsbo\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"xfbouadm bg\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"civpbkhh tl\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"bghgjjyj _button inline gradate rounded\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"components-reconnect-modal\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"components-reconnect-current-attempt\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"components-reconnect-max-retries\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/client-assets/misskey.svg\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"_framework/blazor.web", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/ActivityPub.Misskey.Blazor/css/app", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/ActivityPub.Misskey.Blazor/css/misskey-v12-upstream", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/ActivityPub.Misskey.Blazor/vendor/fontawesome/css/all.min", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"modulepreload\" href=\"_content/ActivityPub.Misskey.Blazor/js/element-size", html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.webassembly.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/dotnet", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mk-app-shell", html, StringComparison.Ordinal);
        string[] scriptTags = html.Split("<script", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(fragment => fragment[..fragment.IndexOf('>')])
            .ToArray();
        Assert.DoesNotContain(scriptTags, tag => tag.Contains("vue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scriptTags, tag => tag.Contains("vite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("data-v-", html, StringComparison.OrdinalIgnoreCase);

        Assert.True(response.Headers.NonValidated.TryGetValues("Set-Cookie", out var cookies));
        string antiforgeryCookie = Assert.Single(
            cookies,
            value => value.StartsWith("__Host-activitypub-oauth-csrf=", StringComparison.Ordinal));
        Assert.Contains("path=/;", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path=/app", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);

        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        const string noncePrefix = "'nonce-";
        int nonceStart = policy.IndexOf(noncePrefix, StringComparison.Ordinal) + noncePrefix.Length;
        int nonceEnd = policy.IndexOf('\'', nonceStart);
        Assert.True(nonceStart >= noncePrefix.Length && nonceEnd > nonceStart);
        string nonce = policy[nonceStart..nonceEnd];
        Assert.Contains($"nonce=\"{nonce}\"", html, StringComparison.Ordinal);

        using HttpResponseMessage secondResponse = await client.GetAsync("/", CancellationToken.None);
        string secondPolicy = Assert.Single(secondResponse.Headers.GetValues("Content-Security-Policy"));
        Assert.DoesNotContain($"'nonce-{nonce}'", secondPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendCredentialMutationsRejectMissingAntiforgeryTokenWithoutReturningServerError()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = new FormUrlEncodedContent([])
        };
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Invalid antiforgery token", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("Hcaptcha")]
    [InlineData("Recaptcha")]
    public async Task CaptchaFrontendCspUsesOnlyTheOfficialProviderOrigins(string provider)
    {
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> captchaFactory = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("KeyManagement:Enabled", "true");
            builder.UseSetting("VaultTransit:Address", "http://127.0.0.1:8200");
            builder.UseSetting("VaultTransit:TokenFile", "/run/secrets/test-vault-token");
            builder.UseSetting("LocalAccounts:RegistrationEnabled", "true");
            builder.UseSetting("RegistrationProtection:CaptchaProvider", provider);
            builder.UseSetting("RegistrationProtection:CaptchaSiteKey", "fixture-site-key");
            builder.UseSetting("RegistrationProtection:CaptchaSecretFile", "/run/secrets/test-captcha-secret");
            builder.UseSetting("RegistrationProtection:CaptchaExpectedHostname", "client.local.example");
        });
        using HttpClient captchaClient = captchaFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://local.example"),
                AllowAutoRedirect = false
            });

        using HttpResponseMessage response = await captchaClient.GetAsync("/", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        if (string.Equals(provider, "Hcaptcha", StringComparison.Ordinal))
        {
            Assert.Contains("connect-src 'self' https://client.local.example https://hcaptcha.com https://*.hcaptcha.com", policy, StringComparison.Ordinal);
            Assert.Contains("frame-src https://hcaptcha.com https://*.hcaptcha.com", policy, StringComparison.Ordinal);
            Assert.Contains("script-src 'self' 'nonce-", policy, StringComparison.Ordinal);
            Assert.Contains("https://hcaptcha.com https://*.hcaptcha.com; style-src 'self' https://hcaptcha.com https://*.hcaptcha.com; style-src-elem 'self' https://hcaptcha.com https://*.hcaptcha.com", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("google.com/recaptcha", policy, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("connect-src 'self' https://client.local.example https://www.google.com/recaptcha/", policy, StringComparison.Ordinal);
            Assert.Contains("frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/", policy, StringComparison.Ordinal);
            Assert.Contains("https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("recaptcha.net", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("hcaptcha.com", policy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryFrontendCredentialMutationPublishesTheBoundedBodyMetadata()
    {
        (string Path, int ValueLengthLimit)[] routes =
        [
            ("/auth/credentials", 2_048),
            ("/auth/passkey/options", 2_048),
            ("/auth/passkey/assertion", 12_288),
            ("/auth/register", 2_048),
            ("/auth/password-reset/request", 2_048),
            ("/auth/password-reset/complete", 2_048),
            ("/auth/email-confirmation/request", 2_048),
            ("/auth/email-confirmation/complete", 2_048),
            ("/auth/logout", 2_048)
        ];
        EndpointDataSource dataSource = fixture.Services.GetRequiredService<EndpointDataSource>();

        foreach ((string route, int valueLengthLimit) in routes)
        {
            RouteEndpoint endpoint = Assert.Single(
                dataSource.Endpoints.OfType<RouteEndpoint>(),
                candidate => string.Equals(candidate.RoutePattern.RawText, route, StringComparison.Ordinal));
            IRequestSizeLimitMetadata requestLimit = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(
                endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>());
            IFormOptionsMetadata formLimit = Assert.IsAssignableFrom<IFormOptionsMetadata>(
                endpoint.Metadata.GetMetadata<IFormOptionsMetadata>());
            Assert.Equal(16_384, requestLimit.MaxRequestBodySize);
            Assert.Equal(16_384, formLimit.BufferBodyLengthLimit);
            Assert.Equal(valueLengthLimit, formLimit.ValueLengthLimit);
            Assert.Equal(16, formLimit.ValueCountLimit);
        }
    }

    [Fact]
    public async Task FrontendCredentialMutationRejectsUnknownLengthBodyAboveTheLimit()
    {
        string antiforgeryToken = await ReadFrontendAntiforgeryTokenAsync();
        string payload = "__RequestVerificationToken=" + Uri.EscapeDataString(antiforgeryToken) +
            "&username=bounded&password=" + new string('x', 20_000);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/credentials")
        {
            Content = new UnknownLengthFormContent(payload),
            Version = HttpVersion.Version11
        };
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add("X-ActivityPub-Frontend", "1");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Request body is too large", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(413, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PasskeyChallengeUsesConfiguredRelyingPartyAndMalformedAssertionIsSingleUse()
    {
        string username = "passkey" + Guid.NewGuid().ToString("N")[..10];
        const string password = "a sufficiently long passkey test password";
        byte[] credentialId = [1, 2, 3, 4, 5, 6, 7, 8];
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LocalIdentityUser user = LocalIdentityUser.Create(username, email: null, now);
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            user.BeginProvisioning(now);
            user.Activate(Guid.NewGuid(), $"https://local.example/users/{username}", now);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, enabled: true)).Succeeded);
            var passkey = new UserPasskeyInfo(
                credentialId,
                publicKey: [0xA1, 0x01, 0x02],
                now,
                signCount: 0,
                transports: ["internal"],
                isUserVerified: true,
                isBackupEligible: false,
                isBackedUp: false,
                attestationObject: [],
                clientDataJson: [])
            {
                Name = "API fixture passkey"
            };
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);
        }

        string antiforgeryToken = await ReadFrontendAntiforgeryTokenAsync();
        using var challengeRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/passkey/options")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["__RequestVerificationToken"] = antiforgeryToken
            })
        };
        challengeRequest.Headers.Add("X-ActivityPub-Frontend", "1");
        using HttpResponseMessage challengeResponse = await client.SendAsync(challengeRequest, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
        Assert.Equal("application/json", challengeResponse.Content.Headers.ContentType?.MediaType);
        string challengeBody = await challengeResponse.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain(password, challengeBody, StringComparison.Ordinal);
        using (JsonDocument challenge = JsonDocument.Parse(challengeBody))
        {
            Assert.Equal("client.local.example", challenge.RootElement.GetProperty("rpId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(challenge.RootElement.GetProperty("challenge").GetString()));
            JsonElement allowed = Assert.Single(challenge.RootElement.GetProperty("allowCredentials").EnumerateArray());
            Assert.Equal("AQIDBAUGBwg", allowed.GetProperty("id").GetString());
        }

        string passkeyCookie = Assert.Single(
            challengeResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-activitypub-passkey-state=", StringComparison.Ordinal));
        Assert.Contains("secure", passkeyCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", passkeyCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", passkeyCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(username, passkeyCookie, StringComparison.Ordinal);
        Assert.DoesNotContain(password, passkeyCookie, StringComparison.Ordinal);

        async Task<HttpResponseMessage> PostMalformedAssertionAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/passkey/assertion")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["credential"] = "{}",
                    ["returnUrl"] = "/",
                    ["__RequestVerificationToken"] = antiforgeryToken
                })
            };
            request.Headers.Add("X-ActivityPub-Frontend", "1");
            return await client.SendAsync(request, CancellationToken.None);
        }

        using HttpResponseMessage malformed = await PostMalformedAssertionAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        using JsonDocument malformedJson = await JsonDocument.ParseAsync(await malformed.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PASSKEY_ASSERTION", malformedJson.RootElement.GetProperty("errorCode").GetString());

        using HttpResponseMessage replay = await PostMalformedAssertionAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using JsonDocument replayJson = await JsonDocument.ParseAsync(await replay.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PASSKEY_ASSERTION", replayJson.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task EmailAvailabilityValidatesSyntaxWithoutDisclosingPersistedAddresses()
    {
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> registrationFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<LocalAccountOptions>();
                services.AddSingleton(new LocalAccountOptions
                {
                    Enabled = true,
                    RegistrationEnabled = true,
                    RequireConfirmedEmail = false
                });
            }));
        using HttpClient registrationClient = registrationFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string email = $"available-{suffix}@client.local.example";

        using (HttpResponseMessage invalid = await registrationClient.GetAsync(
            "/auth/email-address-available?emailAddress=not-an-email",
            CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Contains("no-store", invalid.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            using JsonDocument payload = await JsonDocument.ParseAsync(await invalid.Content.ReadAsStreamAsync());
            Assert.False(payload.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal("format", payload.RootElement.GetProperty("reason").GetString());
        }

        string availableBody;
        using (HttpResponseMessage available = await registrationClient.GetAsync(
            $"/auth/email-address-available?emailAddress={Uri.EscapeDataString(email)}",
            CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.OK, available.StatusCode);
            availableBody = await available.Content.ReadAsStringAsync(CancellationToken.None);
            using JsonDocument payload = JsonDocument.Parse(availableBody);
            Assert.True(payload.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("reason").ValueKind);
        }

        await using (AsyncServiceScope scope = registrationFactory.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = LocalIdentityUser.Create("email" + suffix, email, DateTimeOffset.UtcNow);
            Assert.True((await users.CreateAsync(user, "a sufficiently long email availability password")).Succeeded);
        }

        using HttpResponseMessage used = await registrationClient.GetAsync(
            $"/auth/email-address-available?emailAddress={Uri.EscapeDataString(email)}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        Assert.Equal(availableBody, await used.Content.ReadAsStringAsync(CancellationToken.None));

        using HttpResponseMessage misskeyAvailable = await registrationClient.PostAsJsonAsync(
            "/api/email-address/available",
            new { emailAddress = $"other-{suffix}@client.local.example" });
        using HttpResponseMessage misskeyUsed = await registrationClient.PostAsJsonAsync(
            "/api/email-address/available",
            new { emailAddress = email });
        Assert.Equal(HttpStatusCode.OK, misskeyAvailable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, misskeyUsed.StatusCode);
        Assert.Equal(
            await misskeyAvailable.Content.ReadAsStringAsync(CancellationToken.None),
            await misskeyUsed.Content.ReadAsStringAsync(CancellationToken.None));

        using HttpResponseMessage misskeyUsername = await registrationClient.PostAsJsonAsync(
            "/api/username/available",
            new { username = "email" + suffix });
        Assert.Equal(HttpStatusCode.OK, misskeyUsername.StatusCode);
        using JsonDocument misskeyUsernamePayload = await JsonDocument.ParseAsync(await misskeyUsername.Content.ReadAsStreamAsync());
        Assert.False(misskeyUsernamePayload.RootElement.GetProperty("available").GetBoolean());

        using HttpResponseMessage malformed = await registrationClient.PostAsJsonAsync(
            "/api/email-address/available",
            new { unexpected = true });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using JsonDocument malformedPayload = await JsonDocument.ParseAsync(await malformed.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PARAM", malformedPayload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminInviteEndpointIssuesOnlyThePlaintextResponseAndPersistsItsHash()
    {
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> invitationFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<RegistrationProtectionOptions>();
                services.AddSingleton(new RegistrationProtectionOptions
                {
                    InvitationRequired = true,
                    InvitationLifetime = TimeSpan.FromDays(7),
                    InvitationReservationLifetime = TimeSpan.FromMinutes(5)
                });
            }));
        using HttpClient invitationClient = invitationFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://local.example"),
                AllowAutoRedirect = false
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/invite")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new("Bearer", "fixture-admin");

        using HttpResponseMessage response = await invitationClient.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Single(payload.RootElement.EnumerateObject());
        string code = Assert.IsType<string>(payload.RootElement.GetProperty("code").GetString());
        Assert.Matches("^[2-9A-HJ-NP-Z]{26}$", code);

        await using AsyncServiceScope scope = invitationFactory.Services.CreateAsyncScope();
        IDbContextFactory<LocalIdentityDbContext> identityFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        LocalRegistrationInvitation invitation = await identity.Set<LocalRegistrationInvitation>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.CreatedBy == "fixture-admin");
        Assert.Equal(32, invitation.CodeHash.Length);
        Assert.False(invitation.CodeHash.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(code)));

        IDbContextFactory<FederationDbContext> federationFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        AuditEvent audit = await federation.AuditEvents.AsNoTracking()
            .SingleAsync(candidate => candidate.Action == "registration-invitation-issued" &&
                candidate.Actor == "fixture-admin");
        Assert.DoesNotContain(code, audit.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(invitation.CodeHash), audit.DetailsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MisskeySignupPublishesTheBoundedJsonBodyMetadata()
    {
        EndpointDataSource dataSource = fixture.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint endpoint = Assert.Single(
            dataSource.Endpoints.OfType<RouteEndpoint>(),
            candidate => string.Equals(candidate.RoutePattern.RawText, "/api/signup", StringComparison.Ordinal));
        IRequestSizeLimitMetadata requestLimit = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>());

        Assert.Equal(16_384, requestLimit.MaxRequestBodySize);
    }

    [Theory]
    [InlineData("/auth/password-reset/request")]
    [InlineData("/auth/password-reset/complete")]
    [InlineData("/auth/email-confirmation/request")]
    [InlineData("/auth/email-confirmation/complete")]
    public async Task PasswordAndEmailTokenEndpointsRequireAntiforgeryBeforeProcessing(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent([])
        };
        request.Headers.Add("X-ActivityPub-Frontend", "1");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Invalid antiforgery token", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task EmailConfirmationHttpFlowSetsSessionAndRejectsReplayWithoutLeakingToken()
    {
        string username = "httpcf" + Guid.NewGuid().ToString("N")[..10];
        string email = $"{username}@client.local.example";
        const string password = "a sufficiently long API test password";
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LocalIdentityUser user = LocalIdentityUser.Create(username, email, now);
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            user.BeginProvisioning(now);
            user.Activate(Guid.NewGuid(), $"https://local.example/users/{username}", now);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        string antiforgeryToken = await ReadFrontendAntiforgeryTokenAsync();
        int messagesBefore = fixture.IdentityEmailSender.Confirmations.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/email-confirmation/request")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["email"] = email,
                ["__RequestVerificationToken"] = antiforgeryToken
            })
        };
        request.Headers.Add("X-ActivityPub-Frontend", "1");
        using HttpResponseMessage requested = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Equal("no-store", requested.Headers.CacheControl?.ToString());
        EmailConfirmationEmail emailMessage = Assert.Single(
            fixture.IdentityEmailSender.Confirmations.Skip(messagesBefore));
        Assert.Equal("/signup-complete", emailMessage.ConfirmationUri.AbsolutePath);
        Assert.Empty(emailMessage.ConfirmationUri.Query);
        string confirmationToken = emailMessage.ConfirmationUri.Fragment.TrimStart('#');
        Assert.True(confirmationToken.Length >= 32);
        Assert.DoesNotContain(confirmationToken, await requested.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using HttpResponseMessage completed = await PostConfirmationAsync(confirmationToken, antiforgeryToken);
        string completedBody = await completed.Content.ReadAsStringAsync();
        Assert.True(
            completed.StatusCode == HttpStatusCode.OK,
            $"Unexpected confirmation response {completed.StatusCode}; location={completed.Headers.Location}; body={completedBody}");
        Assert.True(completed.Headers.NonValidated.TryGetValues("Set-Cookie", out var cookies));
        string sessionCookie = Assert.Single(cookies, value =>
            value.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(confirmationToken, sessionCookie, StringComparison.Ordinal);

        await using (AsyncServiceScope verification = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = verification.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            Assert.True((await users.FindByNameAsync(username))?.EmailConfirmed is true);
        }

        string authenticatedAntiforgeryToken = await ReadFrontendAntiforgeryTokenAsync();
        using HttpResponseMessage replay = await PostConfirmationAsync(confirmationToken, authenticatedAntiforgeryToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        string replayBody = await replay.Content.ReadAsStringAsync();
        Assert.DoesNotContain(confirmationToken, replayBody, StringComparison.Ordinal);
        using JsonDocument replayJson = JsonDocument.Parse(replayBody);
        Assert.Equal("INVALID_OR_EXPIRED_TOKEN", replayJson.RootElement.GetProperty("errorCode").GetString());
    }

    private async Task<string> ReadFrontendAntiforgeryTokenAsync()
    {
        using HttpResponseMessage response = await client.GetAsync("/reset-password", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Match match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<HttpResponseMessage> PostConfirmationAsync(string token, string antiforgeryToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/email-confirmation/complete")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["confirmationToken"] = token,
                ["__RequestVerificationToken"] = antiforgeryToken
            })
        };
        request.Headers.Add("X-ActivityPub-Frontend", "1");
        return await client.SendAsync(request, CancellationToken.None);
    }

    private sealed class UnknownLengthFormContent : HttpContent
    {
        private readonly byte[] payload;

        public UnknownLengthFormContent(string body)
        {
            payload = Encoding.UTF8.GetBytes(body);
            Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task AboutMisskeyIsServerRenderedFromThePinnedRazorPort()
    {
        using HttpResponseMessage response = await client.GetAsync("/about-misskey", CancellationToken.None);
        string html = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"_formRoot znqjceqz\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/client-assets/about-icon.png\"", html, StringComparison.Ordinal);
        Assert.Contains("v12.119.2-port.1", html, StringComparison.Ordinal);
        Assert.Equal(32, html.Split("_physics_circle_", StringSplitOptions.None).Length - 1);
        Assert.Contains("https://source.local.example/activitypub-web", html, StringComparison.Ordinal);
        string[] executableTags = html.Split("<script", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(fragment => fragment[..fragment.IndexOf('>')])
            .ToArray();
        Assert.DoesNotContain(executableTags, tag => tag.Contains("vue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(executableTags, tag => tag.Contains("vite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("data-v-", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".vue\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedTimelineIsRenderedOnTheServerFromPersistentData()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/timeline/global");
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
        string html = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-timeline=\"global\"", html, StringComparison.Ordinal);
        Assert.Contains("media-policy-visible-text", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private-fixture-secret", html, StringComparison.Ordinal);
        Assert.DoesNotContain("silenced-public-secret", html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.webassembly.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendStaticAssetIsServedFromRazorClassLibrary()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/_content/ActivityPub.Misskey.Blazor/css/app.css",
            CancellationToken.None);
        string css = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("--accent: #86b300", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/css/app.css", "text/css")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/css/misskey-v12-upstream.css", "text/css")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/vendor/fontawesome/css/all.min.css", "text/css")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/vendor/fontawesome/webfonts/fa-solid-900.woff2", "font/woff2")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/js/theme.js", "text/javascript")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/js/register-service-worker.js", "text/javascript")]
    [InlineData("/_content/ActivityPub.Misskey.Blazor/vendor/matter-0.18.0.min.js", "text/javascript")]
    [InlineData("/_framework/blazor.web.js", "text/javascript")]
    public async Task FrontendBootAssetsAreServedThroughTheConfiguredPathBase(string path, string mediaType)
    {
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpstreamMisskeyClientAssetIsServedFromItsCanonicalRootPath()
    {
        using HttpResponseMessage response = await client.GetAsync("/client-assets/misskey.svg", CancellationToken.None);
        string svg = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServiceWorkerCanControlOnlyTheConfiguredApplicationScopeAndIsNeverCached()
    {
        using HttpResponseMessage worker = await client.GetAsync(
            "/_content/ActivityPub.Misskey.Blazor/service-worker.js",
            CancellationToken.None);
        using HttpResponseMessage registration = await client.GetAsync(
            "/_content/ActivityPub.Misskey.Blazor/js/register-service-worker.js",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
        Assert.Equal("/", Assert.Single(worker.Headers.GetValues("Service-Worker-Allowed")));
        Assert.Contains("no-store", worker.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("scope: '/'", await registration.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendIsServedAtTheApplicationRootWithoutAPathBase()
    {
        using HttpResponseMessage rootResponse = await client.GetAsync("/", CancellationToken.None);
        using HttpResponseMessage loginAliasResponse = await client.GetAsync("/auth/login", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, loginAliasResponse.StatusCode);
    }

    [Fact]
    public async Task FrontendRootIsServerRenderedWithoutARedirectLoop()
    {
        using HttpResponseMessage response = await client.GetAsync("/", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("class=\"mk-app\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MastodonPublicTimelineDoesNotLeakPrivateObjects()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/timelines/public?limit=40", CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("private-fixture-secret", body, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.DoesNotContain("silenced-public-secret", body, StringComparison.Ordinal);
        Assert.Contains("media-policy-visible-text", body, StringComparison.Ordinal);
        JsonElement mediaStatus = Assert.Single(json.RootElement.EnumerateArray(), status =>
            status.GetProperty("content").GetString()?.Contains("media-policy-visible-text", StringComparison.Ordinal) == true);
        Assert.Empty(mediaStatus.GetProperty("media_attachments").EnumerateArray());
    }

    [Fact]
    public async Task ActorUsesConfiguredCanonicalIrisAndCacheValidators()
    {
        using HttpResponseMessage first = await client.GetAsync("/users/alice", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);
        Assert.NotNull(first.Content.Headers.LastModified);
        using JsonDocument actor = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        Assert.Equal("https://local.example/users/alice", actor.RootElement.GetProperty("id").GetString());
        Assert.Equal("https://local.example/users/alice/inbox", actor.RootElement.GetProperty("inbox").GetString());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/users/alice");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag);
        using HttpResponseMessage second = await client.SendAsync(conditional, CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

        using var precedence = new HttpRequestMessage(HttpMethod.Get, "/users/alice");
        precedence.Headers.TryAddWithoutValidation("If-None-Match", "\"different\"");
        precedence.Headers.IfModifiedSince = DateTimeOffset.UtcNow.AddDays(1);
        using HttpResponseMessage third = await client.SendAsync(precedence, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task UnknownWebFingerAccountReturnsNotFoundWithoutEnumerationPayload()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/.well-known/webfinger?resource=acct%3Amissing%40local.example",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
    }

    [Fact]
    public async Task OutboxIsCursorCollectionRatherThanUnboundedArray()
    {
        using HttpResponseMessage response = await client.GetAsync("/users/alice/outbox", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("OrderedCollection", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("https://local.example/users/alice/outbox?page=true", json.RootElement.GetProperty("first").GetString());
        Assert.False(json.RootElement.TryGetProperty("orderedItems", out _));
    }

    [Fact]
    public async Task LocalAndAdministrativeApisRequireBearerAuthentication()
    {
        using var outbox = new HttpRequestMessage(HttpMethod.Post, "/users/alice/outbox")
        {
            Content = JsonContent.Create(new { type = "Note", content = "hello" })
        };
        using HttpResponseMessage outboxResponse = await client.SendAsync(outbox, CancellationToken.None);
        using HttpResponseMessage adminResponse = await client.GetAsync("/admin/dead-letters", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, outboxResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
    }

    [Fact]
    public async Task SignedGetRateLimitRejectsExcessRequests()
    {
        var statuses = new List<HttpStatusCode>();
        for (int index = 0; index < 130; index++)
        {
            using HttpResponseMessage response = await client.GetAsync("/users/alice/followers", CancellationToken.None);
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task VerifiedRemoteActorCanReadFollowingCollectionWithoutPublicCaching()
    {
        const string requestTarget = "/users/alice/following?page=true";
        string date = DateTimeOffset.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);
        string signingString = $"(request-target): get {requestTarget}\nhost: local.example\ndate: {date}";
        byte[] signature = fixture.SignForRecipient(Encoding.ASCII.GetBytes(signingString));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestTarget);
        request.Headers.Date = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation(
            "Signature",
            $"keyId=\"{ActivityPubApiFixture.RecipientKeyIri}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date\",signature=\"{Convert.ToBase64String(signature)}\"");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ActivityPubApiFixture.RecipientActorIri, body, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.Private ?? false);
        Assert.True(response.Headers.CacheControl?.NoStore ?? false);
    }

    [Fact]
    public async Task PrivateObjectDoesNotLeakWithoutRecipientSignatureOrThroughFeaturedCollection()
    {
        using HttpResponseMessage direct = await client.GetAsync(
            $"/objects/{fixture.PrivateObjectId}",
            CancellationToken.None);
        string directBody = await direct.Content.ReadAsStringAsync(CancellationToken.None);
        using HttpResponseMessage featured = await client.GetAsync(
            "/users/alice/featured?page=true",
            CancellationToken.None);
        string featuredBody = await featured.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, direct.StatusCode);
        Assert.True(direct.Headers.CacheControl?.NoStore ?? false);
        Assert.DoesNotContain("private-fixture-secret", directBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, featured.StatusCode);
        Assert.DoesNotContain("private-fixture-secret", featuredBody, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.PrivateObjectId.ToString(), featuredBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrivateObjectIsAvailableToItsSignedRecipientWithoutPublicCaching()
    {
        string requestTarget = $"/objects/{fixture.PrivateObjectId}";
        string date = DateTimeOffset.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);
        string signingString = $"(request-target): get {requestTarget}\nhost: local.example\ndate: {date}";
        byte[] signature = fixture.SignForRecipient(Encoding.ASCII.GetBytes(signingString));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestTarget);
        request.Headers.Date = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation(
            "Signature",
            $"keyId=\"{ActivityPubApiFixture.RecipientKeyIri}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date\",signature=\"{Convert.ToBase64String(signature)}\"");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("private-fixture-secret", body, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.Private ?? false);
        Assert.True(response.Headers.CacheControl?.NoStore ?? false);
    }

    [Fact]
    public async Task MastodonFollowDecisionWithoutAudienceUsesVerifiedEmbeddedFollowRecipient()
    {
        string activityIri = $"{ActivityPubApiFixture.RecipientActorIri}#accepts/follows/{Guid.NewGuid():N}";
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = activityIri,
            type = "Accept",
            actor = ActivityPubApiFixture.RecipientActorIri,
            @object = new
            {
                id = ActivityPubApiFixture.OutboundFollowActivityIri,
                type = "Follow",
                actor = "https://local.example/users/alice",
                @object = ActivityPubApiFixture.RecipientActorIri
            }
        });
        string digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(body));
        string date = DateTimeOffset.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);
        const string requestTarget = "/users/alice/inbox";
        string signingString = $"(request-target): post {requestTarget}\nhost: local.example\ndate: {date}\ndigest: {digest}";
        byte[] signature = fixture.SignForRecipient(Encoding.ASCII.GetBytes(signingString));
        using var request = new HttpRequestMessage(HttpMethod.Post, requestTarget)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/activity+json");
        request.Headers.Date = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("Digest", digest);
        request.Headers.TryAddWithoutValidation(
            "Signature",
            $"keyId=\"{ActivityPubApiFixture.RecipientKeyIri}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest\",signature=\"{Convert.ToBase64String(signature)}\"");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        InboxItem inboxItem = await db.InboxItems.SingleAsync(x => x.ActivityIri == activityIri);
        InboxItemRecipient recipient = await db.InboxItemRecipients.SingleAsync(x => x.InboxItemId == inboxItem.Id);
        Assert.Equal("https://local.example/users/alice", recipient.ActorIri);
    }

    [Fact]
    public async Task MastodonAndMisskeyFollowWithoutAudienceUsesVerifiedObjectRecipient()
    {
        string activityIri = $"{ActivityPubApiFixture.RecipientActorIri}/activities/follow-{Guid.NewGuid():N}";
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = activityIri,
            type = "Follow",
            actor = ActivityPubApiFixture.RecipientActorIri,
            @object = "https://local.example/users/alice"
        });
        string digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(body));
        string date = DateTimeOffset.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);
        const string requestTarget = "/users/alice/inbox";
        string signingString = $"(request-target): post {requestTarget}\nhost: local.example\ndate: {date}\ndigest: {digest}";
        byte[] signature = fixture.SignForRecipient(Encoding.ASCII.GetBytes(signingString));
        using var request = new HttpRequestMessage(HttpMethod.Post, requestTarget)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/activity+json");
        request.Headers.Date = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("Digest", digest);
        request.Headers.TryAddWithoutValidation(
            "Signature",
            $"keyId=\"{ActivityPubApiFixture.RecipientKeyIri}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest\",signature=\"{Convert.ToBase64String(signature)}\"");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        InboxItem inboxItem = await db.InboxItems.SingleAsync(x => x.ActivityIri == activityIri);
        InboxItemRecipient recipient = await db.InboxItemRecipients.SingleAsync(x => x.InboxItemId == inboxItem.Id);
        Assert.Equal("https://local.example/users/alice", recipient.ActorIri);
    }

    [Theory]
    [InlineData("/users/alice/inbox")]
    [InlineData("/inbox")]
    public async Task LikeWithoutAudienceTargetsVerifiedLocalObjectOwner(string requestTarget)
    {
        string activityIri = $"https://remote.example/likes/{Guid.NewGuid():N}";
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = activityIri,
            type = "Like",
            actor = ActivityPubApiFixture.RecipientActorIri,
            @object = $"https://local.example/objects/{fixture.PrivateObjectId}"
        });
        string digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(body));
        string date = DateTimeOffset.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);
        string signingString = $"(request-target): post {requestTarget}\nhost: local.example\ndate: {date}\ndigest: {digest}";
        byte[] signature = fixture.SignForRecipient(Encoding.ASCII.GetBytes(signingString));
        using var request = new HttpRequestMessage(HttpMethod.Post, requestTarget)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/activity+json");
        request.Headers.Date = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("Digest", digest);
        request.Headers.TryAddWithoutValidation(
            "Signature",
            $"keyId=\"{ActivityPubApiFixture.RecipientKeyIri}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date digest\",signature=\"{Convert.ToBase64String(signature)}\"");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        InboxItem inboxItem = await db.InboxItems.SingleAsync(item => item.ActivityIri == activityIri);
        InboxItemRecipient recipient = await db.InboxItemRecipients.SingleAsync(item => item.InboxItemId == inboxItem.Id);
        Assert.Equal("https://local.example/users/alice", recipient.ActorIri);
    }

    [Fact]
    public async Task MastodonReblogOfPublicObjectUsesPublicAnnounceAudience()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/statuses/{fixture.MastodonPublicPostId}/reblog");
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"announce-{Guid.NewGuid():N}");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        ActivityRecord announce = await db.Activities.SingleAsync(x =>
            x.Type == "Announce" && x.ObjectIri == "https://media-blocked.example/objects/1");
        using JsonDocument activity = JsonDocument.Parse(announce.RawJson);
        Assert.Contains(
            activity.RootElement.GetProperty("to").EnumerateArray(),
            value => value.GetString() == "https://www.w3.org/ns/activitystreams#Public");
        Assert.Contains(
            activity.RootElement.GetProperty("cc").EnumerateArray(),
            value => value.GetString() == "https://local.example/users/alice/followers");
    }

    [Fact]
    public async Task CreatedNoteIncludesNormativeAndPleromaOwnerProperties()
    {
        string marker = "pleroma-owner-" + Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new { status = marker, visibility = "public" })
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", marker);

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument status = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Guid objectId = await ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            status.RootElement.GetProperty("id").GetString()!);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        string rawJson = await db.Objects
            .Where(item => item.Id == objectId)
            .Select(item => item.RawJson)
            .SingleAsync();
        using JsonDocument note = JsonDocument.Parse(rawJson);
        Assert.Equal("https://local.example/users/alice", note.RootElement.GetProperty("attributedTo").GetString());
        Assert.Equal("https://local.example/users/alice", note.RootElement.GetProperty("actor").GetString());
    }

    [Fact]
    public async Task CreatedNotePublishesVerifiedMediaTypeAndDimensions()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new
            {
                status = "media metadata fixture",
                visibility = "public",
                media_ids = new[] { fixture.MastodonLocalMediaId }
            })
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"media-{Guid.NewGuid():N}");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument status = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Guid objectId = await ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            status.RootElement.GetProperty("id").GetString()!);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        string rawJson = await db.Objects.Where(item => item.Id == objectId).Select(item => item.RawJson).SingleAsync();
        using JsonDocument note = JsonDocument.Parse(rawJson);
        JsonElement attachment = Assert.Single(note.RootElement.GetProperty("attachment").EnumerateArray());
        Assert.Equal("Image", attachment.GetProperty("type").GetString());
        Assert.Equal("image/png", attachment.GetProperty("mediaType").GetString());
        Assert.Equal(64, attachment.GetProperty("width").GetInt32());
        Assert.Equal(32, attachment.GetProperty("height").GetInt32());
        Assert.Equal(
            Visibility.Public,
            await db.Media.Where(item => item.Id == fixture.LocalMediaId).Select(item => item.Visibility).SingleAsync());
    }

    [Fact]
    public async Task FollowersOnlyStatusUsesPersonalInboxInsteadOfSharedInbox()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string remoteActorIri = $"https://private-follower.example/users/{suffix}";
        string personalInbox = $"https://private-follower.example/users/{suffix}/inbox";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (AsyncServiceScope setupScope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> setupFactory = setupScope.ServiceProvider
                .GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext setup = await setupFactory.CreateDbContextAsync();
            setup.RemoteActors.Add(RemoteActor.Create(
                remoteActorIri,
                "Person",
                suffix,
                $$"""{"id":"{{remoteActorIri}}","type":"Person"}""",
                now));
            setup.RemoteEndpoints.Add(RemoteEndpoint.Create(remoteActorIri, EndpointKind.Inbox, personalInbox, now));
            setup.RemoteEndpoints.Add(RemoteEndpoint.Create(
                remoteActorIri,
                EndpointKind.SharedInbox,
                "https://private-follower.example/inbox",
                now));
            FollowRelation relation = FollowRelation.Request(
                remoteActorIri,
                "https://local.example/users/alice",
                $"https://private-follower.example/activities/{suffix}",
                now);
            relation.Accept(
                "https://local.example/users/alice",
                $"https://local.example/activities/accept-{suffix}",
                now);
            setup.FollowRelations.Add(relation);
            await setup.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new { status = "private delivery fixture", visibility = "private" })
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"private-{suffix}");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument status = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string objectIri = status.RootElement.GetProperty("uri").GetString()!;
        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = verificationScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync();
        Guid activityId = await verification.Activities
            .Where(item => item.ObjectIri == objectIri)
            .Select(item => item.Id)
            .SingleAsync();
        Delivery delivery = await verification.Deliveries
            .SingleAsync(item => item.ActivityId == activityId && item.RemoteDomain == "private-follower.example");
        Assert.Equal(personalInbox, delivery.EndpointIri);
    }

    [Fact]
    public async Task CreatedDirectNoteIncludesResolvedMentionTag()
    {
        string marker = "mention-" + Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new
            {
                status = $"@publisher@media-blocked.example {marker}",
                visibility = "direct"
            })
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", marker);

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument status = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Guid objectId = await ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            status.RootElement.GetProperty("id").GetString()!);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        string rawJson = await db.Objects.Where(item => item.Id == objectId).Select(item => item.RawJson).SingleAsync();
        using JsonDocument note = JsonDocument.Parse(rawJson);
        JsonElement mention = Assert.Single(note.RootElement.GetProperty("tag").EnumerateArray());
        Assert.Equal("Mention", mention.GetProperty("type").GetString());
        Assert.Equal("https://media-blocked.example/users/publisher", mention.GetProperty("href").GetString());
        Assert.Equal("@publisher@media-blocked.example", mention.GetProperty("name").GetString());
    }

    private async Task<Guid> ResolveAsync(ApiDialect dialect, ExternalEntityType entityType, string externalId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        return await externalIds.ResolveAsync(dialect, entityType, externalId, CancellationToken.None)
            ?? throw new InvalidOperationException("The response contained an unresolvable external identifier.");
    }
}
