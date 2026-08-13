using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class MisskeyAuthenticationTests : IDisposable
{
    private static readonly string[] ReadAccountPermission = ["read:account"];
    private static readonly string[] UnsupportedPermission = ["activitypub.admin"];
    private static readonly string[] WriteNotesPermission = ["write:notes"];
    private static readonly string[] WriteVotesPermission = ["write:votes"];
    private static readonly string[] ReadDrivePermission = ["read:drive"];
    private static readonly string[] WriteDrivePermission = ["write:drive"];
    private static readonly string[] PermissionPollChoices = ["allow", "deny"];

    private readonly ActivityPubApiFixture fixture;
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> clientFactory;
    private readonly HttpClient client;

    public MisskeyAuthenticationTests(ActivityPubApiFixture fixture)
    {
        this.fixture = fixture;
        // The product sign-in limiter is intentionally process-local. Give each test its
        // own application instance so one credential/lockout scenario cannot exhaust the
        // IP bucket for an unrelated scenario in the shared PostgreSQL fixture.
        clientFactory = fixture.WithWebHostBuilder(_ => { });
        client = clientFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
    }

    public void Dispose()
    {
        client.Dispose();
        clientFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task V12SigninIssuesMisskeyTokenAndHttpOnlySessionCookie()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "alice",
            password = ActivityPubApiFixture.FixtureAlicePassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.NonValidated.TryGetValues("Set-Cookie", out var setCookies));
        Assert.Contains(setCookies, value => value.StartsWith("__Host-activitypub-oauth-session=", StringComparison.Ordinal));
        CookieAuthenticationOptions cookie = fixture.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(OAuthAuthorizationServerExtensions.ExternalSessionScheme);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string id = json.RootElement.GetProperty("id").GetString()!;
        string token = json.RootElement.GetProperty("i").GetString()!;
        Assert.Equal(fixture.MisskeyLocalActorId, id);
        Assert.StartsWith("mk_", token, StringComparison.Ordinal);
        Assert.DoesNotContain(setCookies, value => value.Contains(token, StringComparison.Ordinal));
        Assert.Equal("succeeded", json.RootElement.GetProperty("status").GetString());

        using HttpResponseMessage me = await client.PostAsJsonAsync("/api/i", new { i = token });
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using JsonDocument account = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("alice", account.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public async Task WasmSessionBootstrapUsesHttpOnlyCookieWithoutExposingItsValue()
    {
        using var browser = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://client.local.example"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using HttpResponseMessage anonymous = await SendFrontendSessionAsync(browser);

        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal("no-store", anonymous.Headers.CacheControl?.ToString());
        Assert.Contains("Cookie", anonymous.Headers.Vary, StringComparer.OrdinalIgnoreCase);
        using (JsonDocument anonymousJson = await JsonDocument.ParseAsync(await anonymous.Content.ReadAsStreamAsync()))
        {
            Assert.False(anonymousJson.RootElement.GetProperty("authenticated").GetBoolean());
            Assert.Equal("X-CSRF-TOKEN", anonymousJson.RootElement.GetProperty("csrf").GetProperty("headerName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                anonymousJson.RootElement.GetProperty("csrf").GetProperty("requestToken").GetString()));
        }

        using HttpResponseMessage signin = await SendFrontendSignInAsync(browser);
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);
        string signinPayload = await signin.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain("\"i\"", signinPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("mk_", signinPayload, StringComparison.Ordinal);

        using HttpResponseMessage authenticated = await SendFrontendSessionAsync(browser);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        string payload = await authenticated.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain("__Host-activitypub-oauth-session", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("mk_", payload, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(payload);
        Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        JsonElement viewer = json.RootElement.GetProperty("viewer");
        Assert.Equal("alice", viewer.GetProperty("username").GetString());
        Assert.Equal("https://local.example/users/alice", viewer.GetProperty("actorIri").GetString());

        using (HttpResponseMessage rejected = await SendFrontendApiAsync(browser, "/api/i"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        JsonElement csrf = json.RootElement.GetProperty("csrf");
        using HttpResponseMessage me = await SendFrontendApiAsync(
            browser,
            "/api/i",
            csrf.GetProperty("headerName").GetString()!,
            csrf.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task SessionCookieIsIgnoredWithoutExplicitFrontendMarker()
    {
        using var browser = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://client.local.example"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using HttpResponseMessage signin = await browser.PostAsJsonAsync("/api/signin", new
        {
            username = "alice",
            password = ActivityPubApiFixture.FixtureAlicePassword
        });
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);

        using HttpResponseMessage response = await browser.GetAsync("/api/frontend/session", CancellationToken.None);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task ExplicitBearerAuthenticationTakesPriorityOverTheFrontendMarker()
    {
        using HttpResponseMessage signin = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "alice",
            password = ActivityPubApiFixture.FixtureAlicePassword
        });
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);
        using JsonDocument signinJson = await JsonDocument.ParseAsync(await signin.Content.ReadAsStreamAsync());
        string token = signinJson.RootElement.GetProperty("i").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/i")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation(
            FrontendBrowserSessionMetadata.RequestHeaderName,
            FrontendBrowserSessionMetadata.RequestHeaderValue);
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        string responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);
    }

    [Fact]
    public async Task V12SigninAcceptsTheMultipartContractUsedByMkSignin()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("alice"), "username");
        form.Add(new StringContent(ActivityPubApiFixture.FixtureAlicePassword), "password");
        form.Add(new StringContent("/app/"), "returnUrl");

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/signin")
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Origin", "https://client.local.example");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("/app/", json.RootElement.GetProperty("redirectUrl").GetString());
        Assert.StartsWith("mk_", json.RootElement.GetProperty("i").GetString(), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SendFrontendSessionAsync(HttpClient browser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/frontend/session");
        request.Headers.TryAddWithoutValidation(
            FrontendBrowserSessionMetadata.RequestHeaderName,
            FrontendBrowserSessionMetadata.RequestHeaderValue);
        return browser.SendAsync(request, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> SendFrontendSignInAsync(HttpClient browser)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/signin")
        {
            Content = JsonContent.Create(new
            {
                username = "alice",
                password = ActivityPubApiFixture.FixtureAlicePassword
            })
        };
        request.Headers.TryAddWithoutValidation(
            FrontendBrowserSessionMetadata.RequestHeaderName,
            FrontendBrowserSessionMetadata.RequestHeaderValue);
        return browser.SendAsync(request, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> SendFrontendApiAsync(
        HttpClient browser,
        string path,
        string? csrfHeaderName = null,
        string? csrfToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation(
            FrontendBrowserSessionMetadata.RequestHeaderName,
            FrontendBrowserSessionMetadata.RequestHeaderValue);
        if (!string.IsNullOrEmpty(csrfHeaderName) && !string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.TryAddWithoutValidation(csrfHeaderName, csrfToken);
        }

        return browser.SendAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task V12SigninRejectsCrossSiteBrowserFormPostsBeforeIssuingCredentials()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = ActivityPubApiFixture.FixtureAlicePassword,
            ["returnUrl"] = "/app/"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/signin")
        {
            Content = form
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.NonValidated.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task V12SigninDoesNotRevealWhetherAnInvalidUsernameExists()
    {
        using HttpResponseMessage known = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "alice",
            password = "invalid-credential"
        });
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "does-not-exist",
            password = "invalid-credential"
        });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(CancellationToken.None),
            await unknown.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FrontendDoesNotExposePreAuthenticationMfaOrPasskeyHints()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/auth/user-hint?username=alice",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownUserSigninExecutesConfiguredPasswordVerificationWork()
    {
        var equalizer = new RecordingPasswordVerificationTimingEqualizer();
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPasswordVerificationTimingEqualizer>();
                services.AddSingleton<IPasswordVerificationTimingEqualizer>(equalizer);
            }));
        using HttpClient timingClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });

        using HttpResponseMessage response = await timingClient.PostAsJsonAsync("/api/signin", new
        {
            username = "unknown-timing-user",
            password = "invalid-credential"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, equalizer.InvocationCount);
    }

    [Fact]
    public async Task MisskeyJsonApiRejectsAnUnknownLengthBodyAboveTheJsonLimit()
    {
        string payload = "{\"userId\":\"" + fixture.MisskeyLocalActorId +
            "\",\"padding\":\"" + new string('x', 2_000_000) + "\"}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/show")
        {
            Content = new UnknownLengthJsonContent(payload),
            Version = HttpVersion.Version11
        };
        request.Headers.TransferEncodingChunked = true;

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task V12SigninReturnsStableErrorIdsAndRejectsMalformedPayload()
    {
        using HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "alice",
            password = "wrong-password"
        });
        Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        using JsonDocument invalidJson = await JsonDocument.ParseAsync(await invalid.Content.ReadAsStreamAsync());
        Assert.Equal("932c904e-9460-45b7-9ce6-7ed33be7eb2c", invalidJson.RootElement.GetProperty("error").GetProperty("id").GetString());

        using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
        using HttpResponseMessage malformedResponse = await client.PostAsync("/api/signin", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);

        using HttpResponseMessage missing = await client.PostAsJsonAsync("/api/signin", new
        {
            username = "does-not-exist",
            password = "not-a-password"
        });
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using JsonDocument missingJson = await JsonDocument.ParseAsync(await missing.Content.ReadAsStreamAsync());
        Assert.Equal("932c904e-9460-45b7-9ce6-7ed33be7eb2c", missingJson.RootElement.GetProperty("error").GetProperty("id").GetString());
    }

    [Fact]
    public async Task V12SigninMapsIdentityTotpAndLockoutResultsToMisskeyContract()
    {
        string totpUsername = "totp" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];
        const string totpPassword = "totp fixture password";
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Guid actorId;
            IDbContextFactory<FederationDbContext> federationFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using (FederationDbContext federation = await federationFactory.CreateDbContextAsync())
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                LocalActor actor = LocalActor.Create(
                    $"https://local.example/users/{totpUsername}",
                    totpUsername,
                    ActorKind.Person,
                    now);
                actor.UpdateProfile("TOTP fixture", "<p>TOTP fixture</p>", false, true, true, now);
                actorId = actor.Id;
                federation.LocalActors.Add(actor);
                await federation.SaveChangesAsync();
            }

            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = LocalIdentityUser.Create(totpUsername, null, DateTimeOffset.UtcNow);
            user.Activate(actorId, $"https://local.example/users/{totpUsername}", DateTimeOffset.UtcNow);
            Assert.True((await users.CreateAsync(user, totpPassword)).Succeeded);
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            string key = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(user));
            try
            {
                ILocalAccountService accountService = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
                LocalAccountAuthenticationResult missingCode = await accountService.AuthenticatePasswordAsync(
                    totpUsername,
                    totpPassword,
                    authenticatorCode: null,
                    CancellationToken.None);
                Assert.Equal(LocalAccountAuthenticationStatus.TwoFactorRequired, missingCode.Status);
                LocalAccountAuthenticationResult invalidCode = await accountService.AuthenticatePasswordAsync(
                    totpUsername,
                    totpPassword,
                    "000000",
                    CancellationToken.None);
                Assert.Equal(LocalAccountAuthenticationStatus.InvalidSecondFactor, invalidCode.Status);

                string code = GenerateAuthenticatorCode(key, DateTimeOffset.UtcNow);
                using HttpResponseMessage validCode = await client.PostAsJsonAsync("/api/signin", new
                {
                    username = totpUsername,
                    password = totpPassword,
                    token = code
                });
                Assert.Equal(HttpStatusCode.OK, validCode.StatusCode);
                using JsonDocument validJson = await JsonDocument.ParseAsync(await validCode.Content.ReadAsStreamAsync());
                Assert.StartsWith("mk_", validJson.RootElement.GetProperty("i").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                await users.SetTwoFactorEnabledAsync(user, false);
                await users.ResetAccessFailedCountAsync(user);
            }
        }

        string lockoutUsername = "lock" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = LocalIdentityUser.Create(lockoutUsername, null, DateTimeOffset.UtcNow);
            Assert.True((await users.CreateAsync(user, "lockout fixture password")).Succeeded);
        }

        using JsonDocument lastBody = await ExerciseLockoutAsync(lockoutUsername);
        Assert.Equal("22d05606-fbcf-421a-a2db-b32610dcfd1b", lastBody.RootElement.GetProperty("error").GetProperty("id").GetString());
        Assert.Equal("TOO_MANY_AUTHENTICATION_FAILURES", lastBody.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task V12SigninPasskeyChallengeUsesMisskeyShapeAndRejectsReplayableMalformedAssertion()
    {
        string username = "passkey" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];
        const string password = "passkey fixture password";
        // Identity's passkey credential id is globally unique in the store; keep
        // this fixture distinct from PublicEndpointTests' [1..8] credential.
        byte[] credentialId = [0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18];
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = LocalIdentityUser.Create(username, null, DateTimeOffset.UtcNow);
            user.Activate(Guid.NewGuid(), $"https://local.example/users/{username}", DateTimeOffset.UtcNow);
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            UserPasskeyInfo passkey = new(
                credentialId,
                publicKey: [0xA1, 0x01, 0x02],
                DateTimeOffset.UtcNow,
                signCount: 0,
                transports: ["internal"],
                isUserVerified: true,
                isBackupEligible: false,
                isBackedUp: false,
                attestationObject: [],
                clientDataJson: []);
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);

            ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
            Assert.True(users.SupportsUserPasskey);
            Assert.True((await accounts.FindAsync(username, CancellationToken.None))?.HasPasskeys);
        }

        // Keep this wire-level WebAuthn test isolated from the shared fixture's IP
        // bucket. Other API tests intentionally consume signin attempts to verify
        // lockout; the product limiter remains unchanged and is still exercised by
        // the dedicated lockout assertions below.
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> passkeyFactory = fixture.WithWebHostBuilder(_ => { });
        using HttpClient passkeyClient = passkeyFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });

        using HttpClient browserPasskeyClient = passkeyFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        using var browserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/signin")
        {
            Content = JsonContent.Create(new { username, password })
        };
        browserRequest.Headers.TryAddWithoutValidation("X-ActivityPub-Frontend", "1");
        using HttpResponseMessage browserChallengeResponse = await browserPasskeyClient.SendAsync(browserRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, browserChallengeResponse.StatusCode);
        using (JsonDocument browserChallenge = await JsonDocument.ParseAsync(
            await browserChallengeResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal("passkey-required", browserChallenge.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                Convert.ToBase64String(credentialId).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                browserChallenge.RootElement.GetProperty("publicKey").GetProperty("allowCredentials")
                    .EnumerateArray().Single().GetProperty("id").GetString());
        }
        Assert.True(browserChallengeResponse.Headers.NonValidated.TryGetValues("Set-Cookie", out var browserChallengeCookies));
        Assert.DoesNotContain(
            browserChallengeCookies,
            value => value.StartsWith("__Host-activitypub-misskey-passkey-challenge=", StringComparison.Ordinal));

        using HttpResponseMessage challengeResponse = await passkeyClient.PostAsJsonAsync("/api/signin", new
        {
            username,
            password
        });
        Assert.True(
            challengeResponse.StatusCode == HttpStatusCode.OK,
            $"Misskey passkey challenge failed with {(int)challengeResponse.StatusCode}: {await challengeResponse.Content.ReadAsStringAsync()}");
        Assert.True(challengeResponse.Headers.NonValidated.TryGetValues("Set-Cookie", out var challengeCookies));
        Assert.Contains(
            challengeCookies,
            value => value.StartsWith("__Host-activitypub-passkey-state=", StringComparison.Ordinal));
        Assert.Contains(
            challengeCookies,
            value => value.StartsWith("__Host-activitypub-misskey-passkey-challenge=", StringComparison.Ordinal));
        using JsonDocument challenge = await JsonDocument.ParseAsync(await challengeResponse.Content.ReadAsStreamAsync());
        string challengeId = challenge.RootElement.GetProperty("challengeId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(challenge.RootElement.GetProperty("challenge").GetString()));
        Assert.Equal(
            Convert.ToHexStringLower(credentialId),
            challenge.RootElement.GetProperty("securityKeys").EnumerateArray().Single().GetProperty("id").GetString());

        using HttpResponseMessage malformed = await passkeyClient.PostAsJsonAsync("/api/signin", new
        {
            username,
            password,
            challengeId,
            credentialId = Convert.ToHexStringLower(credentialId),
            clientDataJSON = "00",
            authenticatorData = "00",
            signature = "00"
        });
        Assert.Equal(HttpStatusCode.Forbidden, malformed.StatusCode);
        using JsonDocument malformedJson = await JsonDocument.ParseAsync(await malformed.Content.ReadAsStreamAsync());
        Assert.Equal("93b86c4b-72f9-40eb-9815-798928603d1e", malformedJson.RootElement.GetProperty("error").GetProperty("id").GetString());
    }

    [Fact]
    public async Task MiAuthSessionNullIssuesAHashedTokenWithoutExposingInternalSession()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/miauth/gen-token")
        {
            Content = JsonContent.Create(new
            {
                session = (string?)null,
                name = "Direct v12 application",
                permission = ReadAccountPermission
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string token = json.RootElement.GetProperty("token").GetString()!;
        Assert.StartsWith("mk_", token, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-z]{10}$", json.RootElement.GetProperty("id").GetString()!);
        Assert.True(json.RootElement.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        using HttpResponseMessage me = await client.PostAsJsonAsync("/api/i", new { i = token });
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        MisskeyAccessToken stored = await db.MisskeyAccessTokens.SingleAsync(item => item.TokenHash == tokenHash);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.NotEqual(Guid.Empty, stored.SourceSessionId);
    }

    [Fact]
    public async Task MiAuthTokenIsHashedConsumedOnceAndAuthenticatesJsonBody()
    {
        string session = Guid.NewGuid().ToString("D");
        string token = await IssueAsync(session, ["read:account", "write:notes"]);

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            MisskeyAccessToken stored = await db.MisskeyAccessTokens.SingleAsync(item => item.SourceSessionId ==
                db.MisskeyAuthSessions.Where(auth => auth.SessionKey == session).Select(auth => auth.Id).Single());
            MisskeyAuthSession authSession = await db.MisskeyAuthSessions.SingleAsync(item => item.SessionKey == session);
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            Assert.Equal(expectedHash, stored.TokenHash);
            Assert.NotEqual(token, stored.TokenHash);
            Assert.DoesNotContain(token, authSession.EncryptedToken ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("Automation client", stored.Name);
            Assert.Equal("MiAuth contract fixture", stored.Description);
            Assert.Equal("https://client.example/icon.png", stored.IconUri);
            AuditEvent audit = await db.AuditEvents.AsNoTracking()
                .SingleAsync(item => item.Category == "misskey-auth" &&
                    item.Action == "token-issued" && item.Target == stored.Id.ToString("N"));
            Assert.DoesNotContain(session, audit.DetailsJson, StringComparison.Ordinal);
            using JsonDocument auditDetails = JsonDocument.Parse(audit.DetailsJson);
            Assert.Equal(authSession.Id, auditDetails.RootElement.GetProperty("sessionId").GetGuid());
        }

        using HttpResponseMessage firstCheck = await client.PostAsJsonAsync($"/api/miauth/{session}/check", new { });
        Assert.Equal(HttpStatusCode.OK, firstCheck.StatusCode);
        using JsonDocument first = await JsonDocument.ParseAsync(await firstCheck.Content.ReadAsStreamAsync());
        Assert.True(first.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(token, first.RootElement.GetProperty("token").GetString());
        Assert.Equal("alice", first.RootElement.GetProperty("user").GetProperty("username").GetString());

        using HttpResponseMessage secondCheck = await client.PostAsJsonAsync($"/api/miauth/{session}/check", new { });
        using JsonDocument second = await JsonDocument.ParseAsync(await secondCheck.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, secondCheck.StatusCode);
        Assert.False(second.RootElement.GetProperty("ok").GetBoolean());

        using HttpResponseMessage me = await client.PostAsJsonAsync("/api/i", new { i = token });
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using JsonDocument meJson = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("alice", meJson.RootElement.GetProperty("username").GetString());

        using HttpResponseMessage note = await client.PostAsJsonAsync("/api/notes/create", new
        {
            i = token,
            text = "MiAuth authenticated note",
            visibility = "public"
        });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
    }

    [Fact]
    public async Task ApplicationListingUsesPersistentMisskeyIdAndRevocationInvalidatesToken()
    {
        string session = Guid.NewGuid().ToString("D");
        string token = await IssueAsync(session, ["read:account"]);

        using HttpResponseMessage applications = await PostAsAliceAsync("/api/i/apps", new { });
        Assert.Equal(HttpStatusCode.OK, applications.StatusCode);
        using JsonDocument applicationsJson = await JsonDocument.ParseAsync(await applications.Content.ReadAsStreamAsync());
        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        string expectedExternalId;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            Guid tokenId = await db.MisskeyAccessTokens
                .Where(item => item.TokenHash == tokenHash)
                .Select(item => item.Id)
                .SingleAsync();
            expectedExternalId = await db.ExternalEntityIds
                .Where(item => item.Dialect == ApiDialect.Misskey &&
                               item.EntityType == ExternalEntityType.AccessToken &&
                               item.InternalId == tokenId)
                .Select(item => item.ExternalId)
                .SingleAsync();
        }

        JsonElement app = applicationsJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == expectedExternalId);
        string externalTokenId = app.GetProperty("id").GetString()!;
        Assert.Matches("^[0-9a-z]{10}$", externalTokenId);
        Assert.Equal("MiAuth contract fixture", app.GetProperty("description").GetString());
        Assert.True(app.GetProperty("expiresAt").GetDateTimeOffset() > app.GetProperty("createdAt").GetDateTimeOffset());

        using HttpResponseMessage revoke = await PostAsAliceAsync("/api/i/revoke-token", new
        {
            tokenId = externalTokenId
        });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using HttpResponseMessage rejected = await client.PostAsJsonAsync("/api/i", new { i = token });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using JsonDocument error = await JsonDocument.ParseAsync(await rejected.Content.ReadAsStreamAsync());
        Assert.Equal("AUTHENTICATION_FAILED", error.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConcurrentSessionCheckReturnsTokenExactlyOnce()
    {
        string session = Guid.NewGuid().ToString("D");
        _ = await IssueAsync(session, ["read:account"]);

        Task<HttpResponseMessage>[] requests = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync($"/api/miauth/{session}/check", new { }))
            .ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        try
        {
            int successful = 0;
            foreach (HttpResponseMessage response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                if (json.RootElement.GetProperty("ok").GetBoolean())
                {
                    successful++;
                }
            }

            Assert.Equal(1, successful);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task MiAuthRejectsUnsupportedPermissionAndSessionMutation()
    {
        string session = Guid.NewGuid().ToString("D");
        using HttpResponseMessage invalidPermission = await PostAsAliceAsync("/api/miauth/gen-token", new
        {
            session,
            name = "Untrusted client",
            permission = UnsupportedPermission
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPermission.StatusCode);

        _ = await IssueAsync(session, ReadAccountPermission);
        using HttpResponseMessage changed = await PostAsAliceAsync("/api/miauth/gen-token", new
        {
            session,
            name = "Changed client",
            permission = ReadAccountPermission
        });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task ScopedTokenCannotEscalatePermissionsOrWriteOutsideGrant()
    {
        string token = await IssueAsync(Guid.NewGuid().ToString("D"), ReadAccountPermission);

        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/notes/create", new
        {
            i = token,
            text = "must not be persisted",
            visibility = "public"
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using JsonDocument permissionError = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        Assert.Equal("PERMISSION_DENIED", permissionError.RootElement.GetProperty("error").GetProperty("code").GetString());

        using HttpResponseMessage escalation = await client.PostAsJsonAsync("/api/miauth/gen-token", new
        {
            i = token,
            session = Guid.NewGuid().ToString("D"),
            name = "Privilege escalation",
            permission = WriteNotesPermission
        });
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Assert.DoesNotContain(await db.Objects.AsNoTracking().ToArrayAsync(), item =>
            item.RawJson.Contains("must not be persisted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AccountAndDriveEndpointsRequireTheirExactMisskeyPermissions()
    {
        string accountReader = await IssueAsync(Guid.NewGuid().ToString("D"), ReadAccountPermission);
        using HttpResponseMessage profileMutation = await client.PostAsJsonAsync("/api/i/update", new
        {
            i = accountReader,
            name = "permission-escalation-must-not-persist"
        });
        Assert.Equal(HttpStatusCode.Forbidden, profileMutation.StatusCode);

        using HttpResponseMessage unrelatedDriveRead = await client.PostAsJsonAsync("/api/drive", new
        {
            i = accountReader
        });
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedDriveRead.StatusCode);

        string driveReader = await IssueAsync(Guid.NewGuid().ToString("D"), ReadDrivePermission);
        using HttpResponseMessage driveRead = await client.PostAsJsonAsync("/api/drive", new
        {
            i = driveReader
        });
        Assert.Equal(HttpStatusCode.OK, driveRead.StatusCode);
        using HttpResponseMessage driveReadMutation = await client.PostAsJsonAsync("/api/drive/folders/create", new
        {
            i = driveReader,
            name = "must-not-be-created"
        });
        Assert.Equal(HttpStatusCode.Forbidden, driveReadMutation.StatusCode);

        string driveWriter = await IssueAsync(Guid.NewGuid().ToString("D"), WriteDrivePermission);
        using HttpResponseMessage driveWrite = await client.PostAsJsonAsync("/api/drive/folders/create", new
        {
            i = driveWriter,
            name = "permission-test"
        });
        Assert.Equal(HttpStatusCode.OK, driveWrite.StatusCode);
        using JsonDocument created = await JsonDocument.ParseAsync(await driveWrite.Content.ReadAsStreamAsync());
        string folderId = created.RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage cleanup = await client.PostAsJsonAsync("/api/drive/folders/delete", new
        {
            i = driveWriter,
            folderId
        });
        Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
    }

    [Fact]
    public async Task PollVoteRequiresExactWriteVotesPermissionWithoutBreakingOidcFallback()
    {
        using HttpResponseMessage create = await PostAsAliceAsync("/api/notes/create", new
        {
            text = "permission-scoped poll",
            visibility = "public",
            poll = new
            {
                choices = PermissionPollChoices,
                multiple = false,
                expiredAfter = 3_600_000
            }
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using JsonDocument created = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        string noteId = created.RootElement.GetProperty("createdNote").GetProperty("id").GetString()!;

        string notesOnly = await IssueAsync(Guid.NewGuid().ToString("D"), WriteNotesPermission);
        using HttpResponseMessage denied = await client.PostAsJsonAsync("/api/notes/polls/vote", new
        {
            i = notesOnly,
            noteId,
            choice = 0
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using JsonDocument deniedJson = await JsonDocument.ParseAsync(await denied.Content.ReadAsStreamAsync());
        Assert.Equal("PERMISSION_DENIED", deniedJson.RootElement.GetProperty("error").GetProperty("code").GetString());

        string voteToken = await IssueAsync(Guid.NewGuid().ToString("D"), WriteVotesPermission);
        using HttpResponseMessage allowed = await client.PostAsJsonAsync("/api/notes/polls/vote", new
        {
            i = voteToken,
            noteId,
            choice = 0
        });
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService ids = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        Guid questionId = await ids.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            CancellationToken.None)
            ?? throw new InvalidOperationException("Created poll did not retain its Misskey identifier mapping.");
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        PollVote vote = await db.PollVotes.AsNoTracking().SingleAsync(value => value.PollId == questionId);
        Assert.Equal("https://local.example/users/alice", vote.VoterActorIri);
        Assert.Equal(0, vote.ChoiceIndex);
    }

    private async Task<JsonDocument> ExerciseLockoutAsync(string username)
    {
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                LocalAccountAuthenticationResult result = await accounts.AuthenticatePasswordAsync(
                    username,
                    "wrong lockout password",
                    authenticatorCode: null,
                    CancellationToken.None);
                Assert.Equal(LocalAccountAuthenticationStatus.InvalidCredentials, result.Status);
            }
        }

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/signin", new
        {
            username,
            password = "wrong lockout password"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        JsonDocument last = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return last;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "This helper reproduces the HMAC-SHA1 TOTP algorithm used by ASP.NET Core Identity; it is test input generation only.")]
    private static string GenerateAuthenticatorCode(string base32Key, DateTimeOffset now)
    {
        byte[] key = DecodeBase32(base32Key);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0f;
        int binaryCode = ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(hash);
        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>((value.Length * 5 + 7) / 8);
        int buffer = 0;
        int bits = 0;
        foreach (char character in value)
        {
            int digit = alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            Assert.InRange(digit, 0, alphabet.Length - 1);
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return [.. bytes];
    }

    private async Task<string> IssueAsync(string session, string[] permissions)
    {
        using HttpResponseMessage response = await PostAsAliceAsync("/api/miauth/gen-token", new
        {
            session,
            name = "Automation client",
            description = "MiAuth contract fixture",
            iconUrl = "https://client.example/icon.png",
            permission = permissions
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string token = json.RootElement.GetProperty("token").GetString()!;
        Assert.StartsWith("mk_", token, StringComparison.Ordinal);
        Assert.True(token.Length >= 40);
        return token;
    }

    private async Task<HttpResponseMessage> PostAsAliceAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        return await client.SendAsync(request);
    }

    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly byte[] payload;

        public UnknownLengthJsonContent(string body)
        {
            payload = Encoding.UTF8.GetBytes(body);
            Headers.TryAddWithoutValidation("Content-Type", "application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class RecordingPasswordVerificationTimingEqualizer : IPasswordVerificationTimingEqualizer
    {
        public int InvocationCount { get; private set; }

        public void VerifyUnknownPassword(string suppliedPassword)
        {
            Assert.Equal("invalid-credential", suppliedPassword);
            InvocationCount++;
        }
    }
}
