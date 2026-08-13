using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.MisskeyApi;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ActivityPub.Server;

internal static class FrontendEndpoints
{
    private const string FrontendHome = "/app/";
    private const long MaximumAuthenticationFormBytes = 16_384;
    private const string MisskeyPasskeyChallengeCookie = "__Host-activitypub-misskey-passkey-challenge";
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFrontendEndpoints(
        this IEndpointRouteBuilder endpoints,
        FrontendOptions options,
        LocalAccountOptions localAccounts,
        RegistrationProtectionOptions registrationProtection,
        PasswordResetOptions passwordReset,
        bool isDevelopment)
    {
        endpoints.MapGet("/api/frontend/config", () =>
        {
            Uri redirectUri = new(options.PublicBaseUri, "/auth/callback");
            Uri postLogoutRedirectUri = new(options.PublicBaseUri, FrontendHome);
            Uri apiBaseUri = new(options.PublicBaseUri, "/api/");
            bool allowInsecureDevelopmentOidc = isDevelopment &&
                options.PublicBaseUri.Scheme == Uri.UriSchemeHttp &&
                options.Authority.Scheme == Uri.UriSchemeHttp;
            return Results.Json(new
            {
                enabled = options.Enabled,
                localAccountsEnabled = localAccounts.Enabled,
                instanceName = options.PublicBaseUri.IdnHost,
                publicBaseUri = options.PublicBaseUri.AbsoluteUri.TrimEnd('/'),
                apiBaseUri = apiBaseUri.AbsoluteUri,
                authority = options.Authority.AbsoluteUri.TrimEnd('/'),
                options.ClientId,
                options.Scopes,
                redirectUri = redirectUri.AbsoluteUri,
                postLogoutRedirectUri = postLogoutRedirectUri.AbsoluteUri,
                sourceUrl = options.SourceUrl?.AbsoluteUri,
                allowInsecureDevelopmentOidc,
                capabilities = new
                {
                    publicTimeline = true,
                    localTimeline = true,
                    homeTimeline = true,
                    compose = true,
                    favourite = true,
                    renote = true,
                    mute = true,
                    mediaUpload = false,
                    notifications = false,
                    streaming = true
                }
            });
        })
        .RequireRateLimiting("discovery");

        endpoints.MapGet(
                "/api/frontend/session",
                (HttpContext context, IAntiforgery antiforgery) => FrontendSession(context, antiforgery))
            .WithMetadata(FrontendBrowserSessionMetadata.Instance)
            .RequireRateLimiting("local-api");

        // Misskey v12 clients post credentials to the instance root API. This route
        // intentionally remains available independently of the Blazor UI switch so
        // headless clients and the server-rendered sign-in form share one contract.
        endpoints.MapPost(
                "/api/signin",
                (HttpContext context,
                    ILocalAccountService accounts,
                    ILocalAccountPrincipalFactory principalFactory,
                    IMisskeyAuthenticationService misskeyAuthentication,
                    IClientApiQueryService clientQuery,
                    IExternalEntityIdService externalIds,
                    LocalAccountOptions accountOptions,
                    CancellationToken cancellationToken) => SignInMisskeyAsync(
                    context,
                    accounts,
                    principalFactory,
                    misskeyAuthentication,
                    clientQuery,
                    externalIds,
                    accountOptions,
                    options.PublicBaseUri,
                    cancellationToken))
            .WithMetadata(new IgnoreAntiforgeryTokenAttribute())
            .WithMetadata(new RequestSizeLimitAttribute(MaximumAuthenticationFormBytes))
            .RequireRateLimiting("misskey-signin");

        if (localAccounts.Enabled)
        {
            endpoints.MapPost(
                    "/api/admin/accounts/create",
                    (HttpContext context,
                        IInitialAdministratorSetupService setup,
                        UserManager<LocalIdentityUser> users,
                        ILocalAccountPrincipalFactory principalFactory,
                        IMisskeyAuthenticationService misskeyAuthentication,
                        MisskeyQueryService misskeyQuery,
                        LocalAccountOptions accountOptions,
                        CancellationToken cancellationToken) => CreateInitialAdministratorAsync(
                            context,
                            setup,
                            users,
                            principalFactory,
                            misskeyAuthentication,
                            misskeyQuery,
                            accountOptions,
                            options.PublicBaseUri,
                            cancellationToken))
                .WithMetadata(new IgnoreAntiforgeryTokenAttribute())
                .WithMetadata(new RequestSizeLimitAttribute(MaximumAuthenticationFormBytes))
                .RequireRateLimiting("initial-setup");
        }

        endpoints.MapGet("/url", async (string url, string? lang, IUrlPreviewService previews, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 2_048)
            {
                return Results.BadRequest();
            }

            UrlPreviewResult? preview = await previews.GetAsync(url, lang, cancellationToken).ConfigureAwait(false);
            if (preview is null)
            {
                return Results.Json(new { });
            }

            return Results.Json(new
            {
                url,
                title = preview.Title,
                description = preview.Description,
                thumbnail = preview.Thumbnail,
                icon = preview.Icon,
                sitename = preview.SiteName,
                player = preview.PlayerUrl is null
                    ? null
                    : new
                    {
                        url = preview.PlayerUrl,
                        width = preview.PlayerWidth,
                        height = preview.PlayerHeight
                    }
            });
        })
        .RequireRateLimiting("local-api");

        if (options.Enabled)
        {
            endpoints.MapGet("/login", (string? returnUrl) =>
                    Results.Redirect(AuthenticationDialogLocation("signin", null, SafeReturnUrl(returnUrl))))
                .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                .RequireRateLimiting("local-api");
            endpoints.MapGet("/auth/login", (string? returnUrl) =>
                    Results.Redirect(AuthenticationDialogLocation("signin", null, SafeReturnUrl(returnUrl))))
                .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                .RequireRateLimiting("local-api");
            if (localAccounts.Enabled)
            {
                endpoints.MapGet("/auth/username-available", UsernameAvailableAsync)
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapGet("/auth/email-address-available", EmailAddressAvailableAsync)
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapPost("/auth/credentials", SignInWithPasswordAsync)
                    .WithBoundedAuthenticationForm()
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapPost("/auth/passkey/options", BeginPasskeyAuthenticationAsync)
                    .WithBoundedAuthenticationForm()
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapPost("/auth/passkey/assertion", CompletePasskeyAuthenticationAsync)
                    .WithBoundedAuthenticationForm(valueLengthLimit: 12_288)
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapPost(
                        "/auth/register",
                        (HttpContext context,
                            IAntiforgery antiforgery,
                            ILocalAccountService accounts,
                            ILocalAccountPrincipalFactory principalFactory,
                            IEmailConfirmationService emailConfirmation,
                            IInitialSetupState initialSetup,
                            LocalAccountOptions accountOptions,
                            CancellationToken cancellationToken) => RegisterAsync(
                                context,
                                antiforgery,
                                accounts,
                                principalFactory,
                                emailConfirmation,
                                initialSetup,
                                accountOptions,
                                registrationProtection,
                                options.PublicBaseUri,
                                cancellationToken))
                    .WithBoundedAuthenticationForm()
                    .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                    .RequireRateLimiting("local-api");
                endpoints.MapPost(
                        "/api/signup",
                        (HttpContext context,
                            ILocalAccountService accounts,
                            IEmailConfirmationService emailConfirmation,
                            IInitialSetupState initialSetup,
                            IMisskeyAuthenticationService misskeyAuthentication,
                            MisskeyQueryService misskeyQuery,
                            LocalAccountOptions accountOptions,
                            CancellationToken cancellationToken) => RegisterMisskeyAsync(
                                context,
                                accounts,
                                emailConfirmation,
                                initialSetup,
                                misskeyAuthentication,
                                misskeyQuery,
                                accountOptions,
                                registrationProtection,
                                options.PublicBaseUri,
                                cancellationToken))
                    .WithMetadata(new RequestSizeLimitAttribute(MaximumAuthenticationFormBytes))
                    .RequireRateLimiting("local-api");
                if (passwordReset.Enabled)
                {
                    endpoints.MapPost(
                            "/auth/password-reset/request",
                            (HttpContext context, IAntiforgery antiforgery, IPasswordResetService service, CancellationToken cancellationToken) =>
                                RequestPasswordResetAsync(context, antiforgery, service, options.PublicBaseUri, cancellationToken))
                        .WithBoundedAuthenticationForm()
                        .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                        .RequireRateLimiting("password-reset-request");
                    endpoints.MapPost("/auth/password-reset/complete", CompletePasswordResetAsync)
                        .WithBoundedAuthenticationForm()
                        .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                        .RequireRateLimiting("password-reset-complete");
                    endpoints.MapPost(
                            "/auth/email-confirmation/request",
                            (HttpContext context, IAntiforgery antiforgery, IEmailConfirmationService service, CancellationToken cancellationToken) =>
                                RequestEmailConfirmationAsync(context, antiforgery, service, options.PublicBaseUri, cancellationToken))
                        .WithBoundedAuthenticationForm()
                        .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                        .RequireRateLimiting("password-reset-request");
                    endpoints.MapPost("/auth/email-confirmation/complete", CompleteEmailConfirmationAsync)
                        .WithBoundedAuthenticationForm()
                        .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                        .RequireRateLimiting("password-reset-complete");
                }
            }
            endpoints.MapPost("/auth/logout", LogoutAsync)
                .WithBoundedAuthenticationForm()
                .WithMetadata(FrontendPathBaseRequiredMetadata.Instance)
                .RequireRateLimiting("local-api");
        }

        return endpoints;
    }

    private static async Task<IResult> UsernameAvailableAsync(
        string? username,
        IRegistrationAvailabilityService accounts,
        LocalAccountOptions options,
        RegistrationProtectionOptions protection,
        CancellationToken cancellationToken)
    {
        if (!protection.RegistrationAvailable(options) || string.IsNullOrWhiteSpace(username))
        {
            return Results.Json(new { available = false });
        }

        bool available = await accounts.IsUsernameAvailableAsync(username, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { available });
    }

    private static async Task<IResult> EmailAddressAvailableAsync(
        HttpContext context,
        string? emailAddress,
        IRegistrationAvailabilityService accounts,
        LocalAccountOptions options,
        RegistrationProtectionOptions protection,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!protection.RegistrationAvailable(options))
        {
            return Results.NotFound();
        }

        RegistrationEmailAvailability result = await accounts
            .CheckEmailAvailabilityAsync(emailAddress ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        string? reason = result.Reason switch
        {
            RegistrationEmailAvailabilityReason.InvalidFormat => "format",
            _ => null
        };
        return Results.Json(new { available = result.Available, reason });
    }

    private static async Task<IResult> SignInWithPasswordAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILocalAccountService accounts,
        ILocalAccountPrincipalFactory principalFactory,
        LocalAccountOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";

        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string returnUrl = SafeReturnUrl(form["returnUrl"]);
        string username = form["username"].ToString();
        string password = form["password"].ToString();
        string token = form["token"].ToString();
        if (username.Length is 0 or > 20 || password.Length is 0 or > 1_024)
        {
            return Results.Redirect(AuthenticationDialogLocation("signin", "INVALID_CREDENTIALS", returnUrl));
        }

        LocalAccountAuthenticationResult result = await accounts
            .AuthenticatePasswordAsync(username, password, token, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == LocalAccountAuthenticationStatus.Succeeded && result.User is not null)
        {
            bool wantsJson = WantsJson(context);
            ClaimsPrincipal principal = await principalFactory.CreateAsync(result.User).ConfigureAwait(false);
            await context.SignInAsync(
                OAuthAuthorizationServerExtensions.ExternalSessionScheme,
                principal,
                new AuthenticationProperties
                {
                    AllowRefresh = false,
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                    RedirectUri = wantsJson ? null : returnUrl
                }).ConfigureAwait(false);
            return wantsJson
                ? Results.Json(new { status = "succeeded", redirectUrl = returnUrl })
                : Results.Redirect(returnUrl);
        }

        string errorCode = result.Status switch
        {
            LocalAccountAuthenticationStatus.LockedOut => "RATE_LIMIT_EXCEEDED",
            LocalAccountAuthenticationStatus.TwoFactorRequired => "TWO_FACTOR_REQUIRED",
            LocalAccountAuthenticationStatus.InvalidSecondFactor => "INVALID_TWO_FACTOR_CODE",
            LocalAccountAuthenticationStatus.AccountNotActive => "ACCOUNT_NOT_ACTIVE",
            LocalAccountAuthenticationStatus.EmailConfirmationRequired => "EMAIL_CONFIRMATION_REQUIRED",
            _ => "INVALID_CREDENTIALS"
        };
        return WantsJson(context)
            ? Results.Json(new { status = ToClientStatus(errorCode), errorCode }, statusCode: StatusCodes.Status401Unauthorized)
            : Results.Redirect(AuthenticationDialogLocation("signin", errorCode, returnUrl));
    }

    private static async Task<IResult> SignInMisskeyAsync(
        HttpContext context,
        ILocalAccountService accounts,
        ILocalAccountPrincipalFactory principalFactory,
        IMisskeyAuthenticationService misskeyAuthentication,
        IClientApiQueryService clientQuery,
        IExternalEntityIdService externalIds,
        LocalAccountOptions options,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled)
        {
            return Results.NotFound();
        }

        // Native Misskey clients do not send browser provenance headers. Browser form
        // submissions do, so reject cross-site navigation before validating credentials or
        // issuing either the session cookie or a durable Misskey token.
        if (!IsTrustedBrowserMutation(context.Request, frontendPublicBaseUri))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        MisskeySignInRequest? request;
        try
        {
            request = await ReadMisskeySignInRequestAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception)
        {
            return Results.StatusCode(exception.StatusCode);
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 ||
            request.Password is null || request.Password.Length > 1_024 ||
            request.Token is not null && request.Token.Length > 128 ||
            request.CredentialId is not null && request.CredentialId.Length > 1_024 ||
            request.ChallengeId is not null && request.ChallengeId.Length > 256 ||
            request.ClientDataJson is not null && request.ClientDataJson.Length > 16_384 ||
            request.AuthenticatorData is not null && request.AuthenticatorData.Length > 16_384 ||
            request.Signature is not null && request.Signature.Length > 16_384 ||
            request.Credential is not null && request.Credential.Length > 12_288)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        LocalAccountLookup? lookup = await accounts.FindAsync(request.Username, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            // Do not bypass the configured password hasher when the username is unknown.
            // AuthenticatePasswordAsync deliberately performs a synthetic verification for
            // this path and returns the same public failure contract as a bad known account.
            LocalAccountAuthenticationResult unknown = await accounts.AuthenticatePasswordAsync(
                request.Username,
                request.Password,
                request.Token,
                cancellationToken).ConfigureAwait(false);
            return MisskeySignInFailure(unknown.Status);
        }

        LocalAccountAuthenticationResult result;
        if (request.HasPasskeyAssertion)
        {
            if (!context.Request.Cookies.TryGetValue(MisskeyPasskeyChallengeCookie, out string? expectedChallengeId) ||
                string.IsNullOrWhiteSpace(expectedChallengeId) ||
                !string.Equals(expectedChallengeId, request.ChallengeId, StringComparison.Ordinal))
            {
                return MisskeySignInError(
                    StatusCodes.Status403Forbidden,
                    "2715a88a-2125-4013-932f-aa6fe72792da");
            }

            context.Response.Cookies.Delete(MisskeyPasskeyChallengeCookie, new CookieOptions
            {
                Path = "/",
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });

            string? credentialJson = request.Credential;
            if (credentialJson is null)
            {
                credentialJson = BuildPasskeyCredentialJson(request);
            }

            if (credentialJson is null)
            {
                return MisskeySignInError(
                    StatusCodes.Status400BadRequest,
                    "93b86c4b-72f9-40eb-9815-798928603d1e");
            }

            LocalPasskeyAuthenticationResult passkey = await accounts
                .AuthenticatePasskeyAsync(credentialJson, cancellationToken)
                .ConfigureAwait(false);
            if (passkey.Status != LocalPasskeyAuthenticationStatus.Succeeded || passkey.User is null)
            {
                return MisskeyPasskeyFailure(passkey.Status);
            }

            result = new(LocalAccountAuthenticationStatus.Succeeded, passkey.User);
        }
        else if (request.Token is null && lookup.TwoFactorEnabled && lookup.HasPasskeys)
        {
            LocalPasskeyChallengeResult challenge = await accounts
                .BeginPasskeyAuthenticationAsync(request.Username, request.Password, cancellationToken)
                .ConfigureAwait(false);
            if (challenge.Status == LocalPasskeyChallengeStatus.Created && challenge.RequestOptionsJson is not null)
            {
                if (WantsJson(context))
                {
                    return BuildFrontendPasskeyChallenge(challenge.RequestOptionsJson);
                }

                return BuildMisskeyPasskeyChallenge(context, challenge.RequestOptionsJson);
            }

            return MisskeyPasskeyChallengeFailure(challenge.Status);
        }
        else
        {
            result = await accounts.AuthenticatePasswordAsync(
                request.Username,
                request.Password,
                request.Token,
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Status != LocalAccountAuthenticationStatus.Succeeded || result.User is null ||
            result.User.LocalActorId is not Guid actorId || string.IsNullOrWhiteSpace(result.User.LocalActorIri))
        {
            return MisskeySignInFailure(result.Status);
        }

        ClientAccountView? account = await clientQuery.FindAccountByIriAsync(
            result.User.LocalActorIri,
            cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return MisskeySignInError(
                StatusCodes.Status403Forbidden,
                "e03a5f46-d309-4865-9b69-56282d94e1eb");
        }

        string externalId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            actorId,
            account.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        bool frontendBrowser = WantsJson(context);
        MisskeyIssuedToken? issued = frontendBrowser
            ? null
            : await misskeyAuthentication.IssueDirectAsync(
                result.User.UserName ?? request.Username,
                "Misskey v12 sign-in",
                description: null,
                iconUri: null,
                MisskeyPermissions.All.ToArray(),
                cancellationToken).ConfigureAwait(false);

        ClaimsPrincipal principal = await principalFactory.CreateAsync(result.User).ConfigureAwait(false);
        string redirectUrl = SafeReturnUrl(request.ReturnUrl);
        await context.SignInAsync(
            OAuthAuthorizationServerExtensions.ExternalSessionScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                RedirectUri = null
            }).ConfigureAwait(false);

        return frontendBrowser
            ? Results.Json(new
            {
                status = "succeeded",
                redirectUrl
            })
            : Results.Json(new
            {
                id = externalId,
                i = issued!.Token,
                status = "succeeded",
                redirectUrl
            });
    }

    private static IResult BuildMisskeyPasskeyChallenge(HttpContext context, string requestOptionsJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestOptionsJson);
            JsonElement root = document.RootElement;
            string challenge = root.GetProperty("challenge").GetString()
                ?? throw new JsonException("Passkey challenge is missing.");
            List<object> securityKeys = [];
            if (root.TryGetProperty("allowCredentials", out JsonElement credentials) && credentials.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement credential in credentials.EnumerateArray())
                {
                    string id = credential.GetProperty("id").GetString()
                        ?? throw new JsonException("Passkey credential id is missing.");
                    securityKeys.Add(new { id = Base64UrlToHex(id) });
                }
            }

            string challengeId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(challenge)));
            context.Response.Cookies.Append(
                MisskeyPasskeyChallengeCookie,
                challengeId,
                new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(2),
                    IsEssential = true
                });
            return Results.Json(new { challenge, challengeId, securityKeys });
        }
        catch (JsonException)
        {
            return MisskeySignInError(StatusCodes.Status409Conflict, "2715a88a-2125-4013-932f-aa6fe72792da");
        }
        catch (FormatException)
        {
            return MisskeySignInError(StatusCodes.Status409Conflict, "2715a88a-2125-4013-932f-aa6fe72792da");
        }
    }

    private static IResult BuildFrontendPasskeyChallenge(string requestOptionsJson)
    {
        try
        {
            JsonNode publicKey = JsonNode.Parse(requestOptionsJson)
                ?? throw new JsonException("Passkey request options are missing.");
            return Results.Json(
                new
                {
                    status = "passkey-required",
                    publicKey
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (JsonException)
        {
            return MisskeySignInError(StatusCodes.Status409Conflict, "2715a88a-2125-4013-932f-aa6fe72792da");
        }
    }

    private static IResult MisskeyPasskeyChallengeFailure(LocalPasskeyChallengeStatus status) => status switch
    {
        LocalPasskeyChallengeStatus.LockedOut => MisskeySignInError(
            StatusCodes.Status429TooManyRequests,
            "22d05606-fbcf-421a-a2db-b32610dcfd1b",
            "TOO_MANY_AUTHENTICATION_FAILURES"),
        LocalPasskeyChallengeStatus.AccountNotActive or LocalPasskeyChallengeStatus.EmailConfirmationRequired =>
            MisskeySignInError(StatusCodes.Status403Forbidden, "e03a5f46-d309-4865-9b69-56282d94e1eb"),
        LocalPasskeyChallengeStatus.PasskeyUnavailable => MisskeySignInError(
            StatusCodes.Status403Forbidden,
            "f27fd449-9af4-4841-9249-1f989b9fa4a4"),
        _ => MisskeySignInError(StatusCodes.Status403Forbidden, "932c904e-9460-45b7-9ce6-7ed33be7eb2c")
    };

    private static IResult MisskeyPasskeyFailure(LocalPasskeyAuthenticationStatus status) => status switch
    {
        LocalPasskeyAuthenticationStatus.LockedOut => MisskeySignInError(
            StatusCodes.Status429TooManyRequests,
            "22d05606-fbcf-421a-a2db-b32610dcfd1b",
            "TOO_MANY_AUTHENTICATION_FAILURES"),
        LocalPasskeyAuthenticationStatus.AccountNotActive or LocalPasskeyAuthenticationStatus.EmailConfirmationRequired =>
            MisskeySignInError(StatusCodes.Status403Forbidden, "e03a5f46-d309-4865-9b69-56282d94e1eb"),
        _ => MisskeySignInError(StatusCodes.Status403Forbidden, "93b86c4b-72f9-40eb-9815-798928603d1e")
    };

    private static string? BuildPasskeyCredentialJson(MisskeySignInRequest request)
    {
        if (request.CredentialId is null || request.ClientDataJson is null ||
            request.AuthenticatorData is null || request.Signature is null)
        {
            return null;
        }

        string credentialId = NormalizePasskeyId(request.CredentialId);
        string? clientDataJson = HexToBase64Url(request.ClientDataJson);
        string? authenticatorData = HexToBase64Url(request.AuthenticatorData);
        string? signature = HexToBase64Url(request.Signature);
        return clientDataJson is null || authenticatorData is null || signature is null
            ? null
            : JsonSerializer.Serialize(new
            {
                id = credentialId,
                rawId = credentialId,
                type = "public-key",
                authenticatorAttachment = (string?)null,
                clientExtensionResults = new { },
                response = new
                {
                    authenticatorData,
                    clientDataJSON = clientDataJson,
                    signature,
                    userHandle = (string?)null
                }
            }, WebJsonOptions);
    }

    private static string NormalizePasskeyId(string value)
    {
        if (value.Length > 4 && value.Length % 2 == 0 && value.All(Uri.IsHexDigit))
        {
            return HexToBase64Url(value) ?? value;
        }

        return value;
    }

    private static string? HexToBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        try
        {
            return Base64Url(Convert.FromHexString(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64UrlToHex(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.ToHexStringLower(Convert.FromBase64String(padded));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IResult MisskeySignInFailure(LocalAccountAuthenticationStatus status) => status switch
    {
        LocalAccountAuthenticationStatus.LockedOut => MisskeySignInError(
            StatusCodes.Status429TooManyRequests,
            "22d05606-fbcf-421a-a2db-b32610dcfd1b",
            "TOO_MANY_AUTHENTICATION_FAILURES"),
        LocalAccountAuthenticationStatus.TwoFactorRequired => Results.Json(
            new
            {
                status = "two-factor-required",
                errorCode = "TWO_FACTOR_REQUIRED",
                error = new { id = "f27fd449-9af4-4841-9249-1f989b9fa4a4" }
            },
            statusCode: StatusCodes.Status401Unauthorized),
        LocalAccountAuthenticationStatus.InvalidSecondFactor => MisskeySignInError(
            StatusCodes.Status403Forbidden,
            "cdf1235b-ac71-46d4-a3a6-84ccce48df6f"),
        LocalAccountAuthenticationStatus.AccountNotActive or LocalAccountAuthenticationStatus.EmailConfirmationRequired =>
            MisskeySignInError(StatusCodes.Status403Forbidden, "e03a5f46-d309-4865-9b69-56282d94e1eb"),
        _ => MisskeySignInError(StatusCodes.Status403Forbidden, "932c904e-9460-45b7-9ce6-7ed33be7eb2c")
    };

    private static IResult MisskeySignInError(int statusCode, string id, string? code = null) =>
        Results.Json(
            new
            {
                error = new
                {
                    id,
                    code,
                    message = code == "TOO_MANY_AUTHENTICATION_FAILURES"
                        ? "Too many failed attempts to sign in. Try again later."
                        : (string?)null
                }
            },
            statusCode: statusCode);

    private static async Task<MisskeySignInRequest?> ReadMisskeySignInRequestAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaximumAuthenticationFormBytes)
        {
            throw new BadHttpRequestException("The sign-in request is too large.", StatusCodes.Status413PayloadTooLarge);
        }

        if (context.Request.HasFormContentType)
        {
            IFormCollection form = await context.Request.ReadFormAsync(new FormOptions
            {
                ValueCountLimit = 16,
                ValueLengthLimit = 2_048,
                KeyLengthLimit = 256,
                MultipartBodyLengthLimit = MaximumAuthenticationFormBytes
            }, cancellationToken).ConfigureAwait(false);
            return new(
                form["username"].ToString(),
                form["password"].ToString(),
                string.IsNullOrEmpty(form["token"].ToString()) ? null : form["token"].ToString(),
                form["returnUrl"].ToString(),
                string.IsNullOrEmpty(form["credentialId"].ToString()) ? null : form["credentialId"].ToString(),
                string.IsNullOrEmpty(form["challengeId"].ToString()) ? null : form["challengeId"].ToString(),
                string.IsNullOrEmpty(form["clientDataJSON"].ToString()) ? null : form["clientDataJSON"].ToString(),
                string.IsNullOrEmpty(form["authenticatorData"].ToString()) ? null : form["authenticatorData"].ToString(),
                string.IsNullOrEmpty(form["signature"].ToString()) ? null : form["signature"].ToString(),
                string.IsNullOrEmpty(form["credential"].ToString()) ? null : form["credential"].ToString());
        }

        if (context.Request.ContentType is not { } contentType ||
            !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var buffer = new MemoryStream();
        byte[] chunk = new byte[4_096];
        int total = 0;
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumAuthenticationFormBytes)
            {
                throw new BadHttpRequestException("The sign-in request is too large.", StatusCodes.Status413PayloadTooLarge);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return JsonSerializer.Deserialize<MisskeySignInRequest>(buffer.ToArray(), WebJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> BeginPasskeyAuthenticationAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILocalAccountService accounts,
        LocalAccountOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.NotFound();
        }

        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string username = form["username"].ToString();
        string password = form["password"].ToString();
        if (username.Length is 0 or > 20 || password.Length is 0 or > 1_024)
        {
            return PasskeyFailure("INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized);
        }

        LocalPasskeyChallengeResult result = await accounts
            .BeginPasskeyAuthenticationAsync(username, password, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == LocalPasskeyChallengeStatus.Created && result.RequestOptionsJson is not null)
        {
            return Results.Content(result.RequestOptionsJson, "application/json");
        }

        return result.Status switch
        {
            LocalPasskeyChallengeStatus.LockedOut => PasskeyFailure("RATE_LIMIT_EXCEEDED", StatusCodes.Status429TooManyRequests),
            LocalPasskeyChallengeStatus.AccountNotActive => PasskeyFailure("ACCOUNT_NOT_ACTIVE", StatusCodes.Status403Forbidden),
            LocalPasskeyChallengeStatus.EmailConfirmationRequired => PasskeyFailure("EMAIL_CONFIRMATION_REQUIRED", StatusCodes.Status403Forbidden),
            LocalPasskeyChallengeStatus.PasskeyUnavailable => PasskeyFailure("PASSKEY_UNAVAILABLE", StatusCodes.Status409Conflict),
            _ => PasskeyFailure("INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized)
        };
    }

    private static async Task<IResult> CompletePasskeyAuthenticationAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILocalAccountService accounts,
        ILocalAccountPrincipalFactory principalFactory,
        LocalAccountOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";

        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string returnUrl = SafeReturnUrl(form["returnUrl"]);
        string credentialJson = form["credential"].ToString();
        if (credentialJson.Length is 0 or > 12_288)
        {
            return PasskeyFailure("INVALID_PASSKEY_ASSERTION", StatusCodes.Status401Unauthorized);
        }

        LocalPasskeyAuthenticationResult result = await accounts
            .AuthenticatePasskeyAsync(credentialJson, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == LocalPasskeyAuthenticationStatus.Succeeded && result.User is not null)
        {
            bool wantsJson = WantsJson(context);
            ClaimsPrincipal principal = await principalFactory.CreateAsync(result.User).ConfigureAwait(false);
            await context.SignInAsync(
                OAuthAuthorizationServerExtensions.ExternalSessionScheme,
                principal,
                new AuthenticationProperties
                {
                    AllowRefresh = false,
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                    RedirectUri = wantsJson ? null : returnUrl
                }).ConfigureAwait(false);
            return wantsJson
                ? Results.Json(new { status = "succeeded", redirectUrl = returnUrl })
                : Results.Redirect(returnUrl);
        }

        return result.Status switch
        {
            LocalPasskeyAuthenticationStatus.LockedOut => PasskeyFailure("RATE_LIMIT_EXCEEDED", StatusCodes.Status429TooManyRequests),
            LocalPasskeyAuthenticationStatus.AccountNotActive => PasskeyFailure("ACCOUNT_NOT_ACTIVE", StatusCodes.Status403Forbidden),
            LocalPasskeyAuthenticationStatus.EmailConfirmationRequired => PasskeyFailure("EMAIL_CONFIRMATION_REQUIRED", StatusCodes.Status403Forbidden),
            LocalPasskeyAuthenticationStatus.PersistenceFailed => PasskeyFailure("PASSKEY_STATE_CONFLICT", StatusCodes.Status409Conflict),
            _ => PasskeyFailure("INVALID_PASSKEY_ASSERTION", StatusCodes.Status401Unauthorized)
        };
    }

    private static IResult PasskeyFailure(string errorCode, int statusCode) =>
        Results.Json(new { status = "failed", errorCode }, statusCode: statusCode);

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILocalAccountService accounts,
        ILocalAccountPrincipalFactory principalFactory,
        IEmailConfirmationService emailConfirmation,
        IInitialSetupState initialSetup,
        LocalAccountOptions options,
        RegistrationProtectionOptions protection,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        if (!protection.RegistrationAvailable(options))
        {
            return Results.NotFound();
        }

        if (await initialSetup.IsRequiredAsync(cancellationToken).ConfigureAwait(false))
        {
            return WantsJson(context)
                ? Results.Json(
                    new { status = "failed", errorCode = "INITIAL_SETUP_REQUIRED" },
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Redirect(FrontendHome);
        }

        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string returnUrl = SafeReturnUrl(form["returnUrl"]);
        string username = form["username"].ToString();
        string email = form["email"].ToString();
        string password = form["password"].ToString();
        string retypedPassword = form["retypedPassword"].ToString();
        if (!string.Equals(password, retypedPassword, StringComparison.Ordinal))
        {
            return Results.Redirect(AuthenticationDialogLocation("signup", "PASSWORD_NOT_MATCHED", returnUrl));
        }

        LocalAccountRegistrationResult result = await accounts
            .RegisterAsync(
                username,
                email,
                password,
                new LocalRegistrationProtection(
                    form["invitationCode"].ToString(),
                    form["hcaptcha-response"].ToString(),
                    form["g-recaptcha-response"].ToString(),
                    context.Connection.RemoteIpAddress?.ToString(),
                    form["cf-turnstile-response"].ToString()),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == LocalAccountRegistrationStatus.Created && result.User is not null)
        {
            if (options.RequireConfirmedEmail)
            {
                await emailConfirmation.RequestForUserAsync(
                    result.User,
                    frontendPublicBaseUri,
                    cancellationToken).ConfigureAwait(false);
                return WantsJson(context)
                    ? Results.Json(new { status = "signup-email-pending" })
                    : Results.Redirect(AuthenticationDialogLocation("signup-pending", null, returnUrl));
            }

            bool wantsJson = WantsJson(context);
            ClaimsPrincipal principal = await principalFactory.CreateAsync(result.User).ConfigureAwait(false);
            await context.SignInAsync(
                OAuthAuthorizationServerExtensions.ExternalSessionScheme,
                principal,
                new AuthenticationProperties
                {
                    AllowRefresh = false,
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                    RedirectUri = wantsJson ? null : returnUrl
                }).ConfigureAwait(false);
            return wantsJson
                ? Results.Json(new { status = "succeeded", redirectUrl = returnUrl })
                : Results.Redirect(returnUrl);
        }

        string errorCode = result.SafeErrorCodes.Count == 0
            ? "REGISTRATION_FAILED"
            : result.SafeErrorCodes[0];
        if (result.Status == LocalAccountRegistrationStatus.EmailUnavailable)
        {
            // The form must not confirm that an address is already registered. Operators can
            // correlate the generic failure with the structured, access-controlled audit trail.
            errorCode = "REGISTRATION_FAILED";
        }
        return WantsJson(context)
            ? Results.Json(new { status = "failed", errorCode }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Redirect(AuthenticationDialogLocation("signup", errorCode, returnUrl));
    }

    private static async Task<IResult> RegisterMisskeyAsync(
        HttpContext context,
        ILocalAccountService accounts,
        IEmailConfirmationService emailConfirmation,
        IInitialSetupState initialSetup,
        IMisskeyAuthenticationService misskeyAuthentication,
        MisskeyQueryService misskeyQuery,
        LocalAccountOptions options,
        RegistrationProtectionOptions protection,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        if (!protection.RegistrationAvailable(options) ||
            context.Request.ContentType is null ||
            !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            context.Request.ContentLength is > MaximumAuthenticationFormBytes)
        {
            return Results.BadRequest();
        }

        if (await initialSetup.IsRequiredAsync(cancellationToken).ConfigureAwait(false))
        {
            return InitialSetupError(
                StatusCodes.Status409Conflict,
                "INITIAL_SETUP_REQUIRED",
                "f6e22d10-fd44-47c8-bc17-45f01c8f0d21");
        }

        MisskeySignupRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<MisskeySignupRequest>(
                context.Request.Body,
                WebJsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (request is null)
        {
            return Results.BadRequest();
        }

        LocalAccountRegistrationResult result = await accounts.RegisterAsync(
            request.Username ?? string.Empty,
            request.EmailAddress,
            request.Password ?? string.Empty,
            new LocalRegistrationProtection(
                request.InvitationCode,
                request.HcaptchaResponse,
                request.RecaptchaResponse,
                context.Connection.RemoteIpAddress?.ToString(),
                request.TurnstileResponse),
            cancellationToken).ConfigureAwait(false);
        if (result.Status != LocalAccountRegistrationStatus.Created || result.User is null)
        {
            return Results.BadRequest();
        }

        if (options.RequireConfirmedEmail)
        {
            await emailConfirmation.RequestForUserAsync(
                result.User,
                frontendPublicBaseUri,
                cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }

        MisskeyIssuedToken issued = await misskeyAuthentication.IssueAsync(
            result.User.UserName!,
            Guid.NewGuid().ToString("D"),
            "Misskey web client",
            "Native token issued after local account registration.",
            iconUri: null,
            callbackUri: null,
            MisskeyPermissions.All,
            cancellationToken).ConfigureAwait(false);
        object? account = await misskeyQuery.FindMeAsync(result.User.UserName!, cancellationToken).ConfigureAwait(false);
        JsonObject response = JsonSerializer.SerializeToNode(account, WebJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("The newly registered Misskey account could not be projected.");
        response["token"] = issued.Token;
        return Results.Json(response);
    }

    private static async Task<IResult> CreateInitialAdministratorAsync(
        HttpContext context,
        IInitialAdministratorSetupService setup,
        UserManager<LocalIdentityUser> users,
        ILocalAccountPrincipalFactory principalFactory,
        IMisskeyAuthenticationService misskeyAuthentication,
        MisskeyQueryService misskeyQuery,
        LocalAccountOptions options,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled || !IsTrustedBrowserMutation(context.Request, frontendPublicBaseUri) ||
            context.Request.ContentType is null ||
            !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            context.Request.ContentLength is > MaximumAuthenticationFormBytes)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        InitialAdministratorRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<InitialAdministratorRequest>(
                context.Request.Body,
                WebJsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return InitialSetupError(
                StatusCodes.Status400BadRequest,
                "INVALID_PARAM",
                "3d81ceae-475f-4600-b2a8-2bc116157532");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username) ||
            request.Username.Length > 20 || request.Password is null || request.Password.Length > 1_024)
        {
            return InitialSetupError(
                StatusCodes.Status400BadRequest,
                "INVALID_PARAM",
                "3d81ceae-475f-4600-b2a8-2bc116157532");
        }

        InitialAdministratorSetupResult result = await setup.CreateAsync(
            request.Username,
            request.Password,
            cancellationToken).ConfigureAwait(false);
        if (result.Status != InitialAdministratorSetupStatus.Created || result.UserId is not Guid userId ||
            string.IsNullOrWhiteSpace(result.Username))
        {
            string code = result.SafeErrorCodes.Count > 0
                ? result.SafeErrorCodes[0]
                : "INITIAL_SETUP_FAILED";
            int status = result.Status == InitialAdministratorSetupStatus.AlreadyInitialized
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            return InitialSetupError(status, code, "0b5f5c5b-766b-4df5-96f1-01572e720b30");
        }

        LocalIdentityUser user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The initialized administrator identity could not be loaded.");
        MisskeyIssuedToken issued = await misskeyAuthentication.IssueDirectAsync(
            result.Username,
            "Misskey v12 initial setup",
            "Native token issued when the initial administrator was created.",
            iconUri: null,
            MisskeyPermissions.All,
            cancellationToken).ConfigureAwait(false);
        object? account = await misskeyQuery.FindMeAsync(result.Username, cancellationToken).ConfigureAwait(false);
        JsonObject response = JsonSerializer.SerializeToNode(account, WebJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("The initialized administrator could not be projected.");
        response["token"] = issued.Token;

        ClaimsPrincipal principal = await principalFactory.CreateAsync(user).ConfigureAwait(false);
        await context.SignInAsync(
            OAuthAuthorizationServerExtensions.ExternalSessionScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                RedirectUri = null
            }).ConfigureAwait(false);
        return Results.Json(response);
    }

    private static IResult InitialSetupError(int statusCode, string code, string id) =>
        Results.Json(
            new
            {
                error = new
                {
                    message = "Initial administrator setup could not be completed.",
                    code,
                    id,
                    kind = "client"
                }
            },
            statusCode: statusCode);

    private sealed class MisskeySignupRequest
    {
        public string? Username { get; init; }

        public string? Password { get; init; }

        public string? EmailAddress { get; init; }

        public string? InvitationCode { get; init; }

        [JsonPropertyName("hcaptcha-response")]
        public string? HcaptchaResponse { get; init; }

        [JsonPropertyName("g-recaptcha-response")]
        public string? RecaptchaResponse { get; init; }

        [JsonPropertyName("cf-turnstile-response")]
        public string? TurnstileResponse { get; init; }
    }

    private sealed class InitialAdministratorRequest
    {
        public string? Username { get; init; }

        public string? Password { get; init; }
    }

    private static async Task<IFormCollection?> ReadBoundedFormAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
        {
            return null;
        }

        if (context.Request.ContentLength is > MaximumAuthenticationFormBytes)
        {
            throw new BadHttpRequestException(
                "Request body is too large.",
                StatusCodes.Status413PayloadTooLarge);
        }

        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false } &&
            (sizeFeature.MaxRequestBodySize is null || sizeFeature.MaxRequestBodySize > MaximumAuthenticationFormBytes))
        {
            sizeFeature.MaxRequestBodySize = MaximumAuthenticationFormBytes;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            return await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new BadHttpRequestException(
                "Request form exceeds the authentication limits.",
                StatusCodes.Status413PayloadTooLarge,
                exception);
        }
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IPasswordResetService passwordReset,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string username = form["username"].ToString();
        string email = form["email"].ToString();
        await passwordReset.RequestAsync(username, email, frontendPublicBaseUri, cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        return WantsJson(context)
            ? Results.Json(new { status = "accepted" }, statusCode: StatusCodes.Status202Accepted)
            : Results.Redirect(FrontendHome);
    }

    private static async Task<IResult> CompletePasswordResetAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IPasswordResetService passwordReset,
        CancellationToken cancellationToken)
    {
        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        PasswordResetCompletionResult result = await passwordReset.ResetAsync(
            form["resetToken"].ToString(),
            form["password"].ToString(),
            cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        if (result.Status == PasswordResetCompletionStatus.Succeeded)
        {
            return WantsJson(context)
                ? Results.Json(new { status = "succeeded", redirectUrl = FrontendHome })
                : Results.Redirect(FrontendHome);
        }

        string errorCode = result.SafeErrorCodes.Count == 0
            ? "PASSWORD_RESET_FAILED"
            : result.SafeErrorCodes[0];
        int status = result.Status == PasswordResetCompletionStatus.Disabled
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return WantsJson(context)
            ? Results.Json(new { status = "failed", errorCode }, statusCode: status)
            : Results.Redirect("/app/reset-password?resetError=" + Uri.EscapeDataString(errorCode));
    }

    private static async Task<IResult> RequestEmailConfirmationAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IEmailConfirmationService confirmation,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken)
    {
        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        await confirmation.RequestAsync(
            form["username"].ToString(),
            form["email"].ToString(),
            frontendPublicBaseUri,
            cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        return WantsJson(context)
            ? Results.Json(new { status = "accepted" }, statusCode: StatusCodes.Status202Accepted)
            : Results.Redirect(FrontendHome);
    }

    private static async Task<IResult> CompleteEmailConfirmationAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IEmailConfirmationService confirmation,
        ILocalAccountPrincipalFactory principalFactory,
        LocalAccountOptions options,
        CancellationToken cancellationToken)
    {
        IFormCollection? form = await ReadBoundedFormAsync(context, antiforgery, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        EmailConfirmationResult result = await confirmation.ConfirmAsync(
            form["confirmationToken"].ToString(),
            cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        if (result.Status != EmailConfirmationStatus.Succeeded || result.User is null)
        {
            int status = result.Status == EmailConfirmationStatus.Disabled
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return WantsJson(context)
                ? Results.Json(new { status = "failed", errorCode = "INVALID_OR_EXPIRED_TOKEN" }, statusCode: status)
                : Results.Redirect("/app/signup-complete?confirmationError=INVALID_OR_EXPIRED_TOKEN");
        }

        bool wantsJson = WantsJson(context);
        ClaimsPrincipal principal = await principalFactory.CreateAsync(result.User).ConfigureAwait(false);
        await context.SignInAsync(
            OAuthAuthorizationServerExtensions.ExternalSessionScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
                RedirectUri = wantsJson ? null : "/"
            }).ConfigureAwait(false);
        return wantsJson
            ? Results.Json(new { status = "succeeded", redirectUrl = FrontendHome })
            : Results.Redirect(FrontendHome);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        await context.SignOutAsync(OAuthAuthorizationServerExtensions.ExternalSessionScheme).ConfigureAwait(false);
        return Results.Redirect(FrontendHome);
    }

    private static string SafeReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') ||
            value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\') ||
            value.Contains("://", StringComparison.Ordinal))
        {
            return FrontendHome;
        }

        return value;
    }

    private static bool IsTrustedBrowserMutation(HttpRequest request, Uri frontendPublicBaseUri)
    {
        if (request.Headers.TryGetValue("Sec-Fetch-Site", out var fetchSites))
        {
            if (fetchSites.Count != 1)
            {
                return false;
            }

            string fetchSite = fetchSites[0] ?? string.Empty;
            if (!string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!request.Headers.TryGetValue("Origin", out var origins))
        {
            return true;
        }

        if (origins.Count != 1 ||
            !Uri.TryCreate(origins[0], UriKind.Absolute, out Uri? origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            return false;
        }

        return string.Equals(origin.Scheme, frontendPublicBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(origin.IdnHost, frontendPublicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            origin.Port == frontendPublicBaseUri.Port;
    }

    private static string AuthenticationDialogLocation(string dialog, string? errorCode, string returnUrl)
    {
        string location = $"{FrontendHome}?auth={Uri.EscapeDataString(dialog)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return string.IsNullOrEmpty(errorCode)
            ? location
            : location + "&authError=" + Uri.EscapeDataString(errorCode);
    }

    private static bool WantsJson(HttpContext context) =>
        string.Equals(context.Request.Headers["X-ActivityPub-Frontend"], "1", StringComparison.Ordinal);

    private static IResult FrontendSession(HttpContext context, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Vary = "Cookie";

        ClaimsPrincipal principal = context.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Results.Json(new
            {
                authenticated = false,
                csrf = new
                {
                    headerName = tokens.HeaderName,
                    requestToken = tokens.RequestToken
                }
            });
        }

        string? username = principal.FindFirst("preferred_username")?.Value ?? principal.Identity.Name;
        string? actorIri = principal.FindFirst(LocalAccountServiceCollectionExtensions.LocalActorClaim)?.Value;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(actorIri))
        {
            return Results.Json(new
            {
                authenticated = false,
                csrf = new
                {
                    headerName = tokens.HeaderName,
                    requestToken = tokens.RequestToken
                }
            });
        }

        string[] roles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(role => !string.IsNullOrWhiteSpace(role) && role.Length <= 128 && !role.Any(char.IsControl))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return Results.Json(new
        {
            authenticated = true,
            viewer = new
            {
                username,
                actorIri,
                roles
            },
            csrf = new
            {
                headerName = tokens.HeaderName,
                requestToken = tokens.RequestToken
            }
        });
    }

    private static string ToClientStatus(string errorCode) => errorCode == "TWO_FACTOR_REQUIRED"
        ? "two-factor-required"
        : "failed";

    private static RouteHandlerBuilder WithBoundedAuthenticationForm(
        this RouteHandlerBuilder builder,
        int valueLengthLimit = 2_048) =>
        builder.WithMetadata(
            new RequestSizeLimitAttribute(MaximumAuthenticationFormBytes),
            new RequestFormLimitsAttribute
            {
                BufferBody = true,
                BufferBodyLengthLimit = MaximumAuthenticationFormBytes,
                MemoryBufferThreshold = (int)MaximumAuthenticationFormBytes,
                KeyLengthLimit = 256,
                ValueLengthLimit = valueLengthLimit,
                ValueCountLimit = 16,
                MultipartBodyLengthLimit = MaximumAuthenticationFormBytes,
                MultipartHeadersCountLimit = 8,
                MultipartHeadersLengthLimit = 4_096
            },
            BoundedAuthenticationFormMetadata.Instance);

    public static IApplicationBuilder UseFrontendAssets(
        this IApplicationBuilder app,
        FrontendOptions options,
        RegistrationProtectionOptions registrationProtection)
    {
        if (!options.Enabled)
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/app/service-worker.js"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Service-Worker-Allowed"] = FrontendHome;
                    context.Response.Headers.CacheControl = "no-cache,no-store";
                    return Task.CompletedTask;
                });
            }

            context.Response.OnStarting(() =>
            {
                string authorityOrigin = options.Authority.GetLeftPart(UriPartial.Authority);
                string captchaConnect = registrationProtection.CaptchaProvider switch
                {
                    RegistrationCaptchaProvider.Hcaptcha => " https://hcaptcha.com https://*.hcaptcha.com",
                    RegistrationCaptchaProvider.Recaptcha => " https://www.google.com/recaptcha/",
                    RegistrationCaptchaProvider.Turnstile => " https://challenges.cloudflare.com",
                    _ => string.Empty
                };
                string captchaScript = registrationProtection.CaptchaProvider switch
                {
                    RegistrationCaptchaProvider.Hcaptcha => " https://hcaptcha.com https://*.hcaptcha.com",
                    RegistrationCaptchaProvider.Recaptcha =>
                        " https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/",
                    RegistrationCaptchaProvider.Turnstile => " https://challenges.cloudflare.com",
                    _ => string.Empty
                };
                string captchaFrame = registrationProtection.CaptchaProvider switch
                {
                    RegistrationCaptchaProvider.Hcaptcha =>
                        "frame-src https://hcaptcha.com https://*.hcaptcha.com; ",
                    RegistrationCaptchaProvider.Recaptcha =>
                        "frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/; ",
                    RegistrationCaptchaProvider.Turnstile =>
                        "frame-src https://challenges.cloudflare.com; ",
                    _ => string.Empty
                };
                string captchaStyle = registrationProtection.CaptchaProvider == RegistrationCaptchaProvider.Hcaptcha
                    ? " https://hcaptcha.com https://*.hcaptcha.com"
                    : string.Empty;
                // Misskey's layout, motion, theme variables, media geometry, and pointer ripples
                // require runtime style attributes. Keep executable script strict and limit the
                // exception to style attributes; stylesheet elements still have to come from
                // this origin. Every value written by our interop boundary is allow-listed or
                // numeric (see theme.js, modal.js, and button-ripple.js).
                context.Response.Headers.ContentSecurityPolicy =
                    $"default-src 'self'; base-uri 'self'; connect-src 'self' {authorityOrigin}{captchaConnect}; font-src 'self'; form-action 'self'; {captchaFrame}frame-ancestors 'none'; img-src 'self' data:; object-src 'none'; script-src 'self' 'wasm-unsafe-eval'{captchaScript}; style-src 'self'{captchaStyle}; style-src-elem 'self'{captchaStyle}; style-src-attr 'unsafe-inline'; upgrade-insecure-requests";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
                return Task.CompletedTask;
            });

            await next().ConfigureAwait(false);
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                bool immutableAsset = context.File.Name.Contains('-', StringComparison.Ordinal) &&
                    !context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase);
                context.Context.Response.Headers.CacheControl = immutableAsset
                    ? "public,max-age=31536000,immutable"
                    : "no-cache";
            }
        });
        return app;
    }
}

internal sealed class FrontendPathBaseRequiredMetadata
{
    public static FrontendPathBaseRequiredMetadata Instance { get; } = new();

    private FrontendPathBaseRequiredMetadata()
    {
    }
}

internal sealed class BoundedAuthenticationFormMetadata
{
    public static BoundedAuthenticationFormMetadata Instance { get; } = new();

    private BoundedAuthenticationFormMetadata()
    {
    }
}

internal sealed record MisskeySignInRequest(
    string Username,
    string Password,
    string? Token,
    string? ReturnUrl,
    string? CredentialId = null,
    string? ChallengeId = null,
    string? ClientDataJson = null,
    string? AuthenticatorData = null,
    string? Signature = null,
    string? Credential = null)
{
    public bool HasPasskeyAssertion =>
        !string.IsNullOrWhiteSpace(Credential) ||
        !string.IsNullOrWhiteSpace(CredentialId) ||
        !string.IsNullOrWhiteSpace(ChallengeId) ||
        !string.IsNullOrWhiteSpace(ClientDataJson) ||
        !string.IsNullOrWhiteSpace(AuthenticatorData) ||
        !string.IsNullOrWhiteSpace(Signature);
}
