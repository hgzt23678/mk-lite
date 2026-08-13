using System.Net;
using System.Threading.RateLimiting;
using ActivityPub.Application;
using ActivityPub.Federation;
using ActivityPub.Identity;
using ActivityPub.MastodonApi;
using ActivityPub.Media;
using ActivityPub.Misskey.Blazor;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.MisskeyApi;
using ActivityPub.Moderation;
using ActivityPub.Operations;
using ActivityPub.Persistence;
using ActivityPub.Server;
using ActivityPub.Workers;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
bool isProduction = builder.Environment.IsProduction();
FederationOptions federationOptions = ConfigurationReader.ReadFederation(builder.Configuration);
WorkerOptions workerOptions = ConfigurationReader.ReadWorkers(builder.Configuration);
ApiAuthenticationOptions authenticationOptions = ConfigurationReader.ReadAuthentication(builder.Configuration);
MisskeyAuthenticationOptions misskeyAuthenticationOptions = ConfigurationReader.ReadMisskeyAuthentication(builder.Configuration);
OAuthAuthorizationServerOptions oauthOptions = ConfigurationReader.ReadOAuth(builder.Configuration);
LocalAccountOptions localAccountOptions = ConfigurationReader.ReadLocalAccounts(builder.Configuration);
RegistrationProtectionOptions registrationProtectionOptions = ConfigurationReader.ReadRegistrationProtection(builder.Configuration);
PasswordResetOptions passwordResetOptions = ConfigurationReader.ReadPasswordReset(builder.Configuration);
FrontendOptions frontendOptions = ConfigurationReader.ReadFrontend(
    builder.Configuration,
    federationOptions.PublicBaseUri,
    authenticationOptions.Authority);
bool hostFrontend = frontendOptions.Enabled && !StartupCommandClassifier.IsMaintenanceCommand(args);
MediaOptions mediaOptions = ConfigurationReader.ReadMedia(builder.Configuration);
SpamEvaluationOptions spamOptions = ConfigurationReader.ReadSpamEvaluation(builder.Configuration);
StreamingOptions streamingOptions = builder.Configuration.GetSection("Streaming").Get<StreamingOptions>() ?? new();
ConfigurationReader.ValidateProductionConfiguration(builder.Configuration, federationOptions, authenticationOptions, isProduction);
oauthOptions.Validate(isProduction);
if (hostFrontend)
{
    frontendOptions.Validate(isProduction);
}

if (hostFrontend && !oauthOptions.Enabled)
{
    throw new InvalidOperationException("The server-rendered frontend requires OAuth:Enabled.");
}

if (hostFrontend && !string.Equals(oauthOptions.CallbackPath, "/auth/callback", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The server-rendered frontend requires OAuth:CallbackPath=/auth/callback.");
}

bool keyManagementEnabled = builder.Configuration.GetValue("KeyManagement:Enabled", workerOptions.DeliveryEnabled);
streamingOptions.Validate();
localAccountOptions.Validate(isProduction, keyManagementEnabled);
registrationProtectionOptions.Validate(localAccountOptions, isProduction);
if (registrationProtectionOptions.InvitationRequired && !keyManagementEnabled)
{
    throw new InvalidOperationException("Invitation registration requires KeyManagement:Enabled.");
}
if (isProduction && registrationProtectionOptions.InvitationRequired && !localAccountOptions.RequireConfirmedEmail)
{
    throw new InvalidOperationException("Production invitation registration requires LocalAccounts:RequireConfirmedEmail=true.");
}
passwordResetOptions.Validate(isProduction, localAccountOptions, frontendOptions.PublicBaseUri);
if (localAccountOptions.Enabled && !hostFrontend)
{
    throw new InvalidOperationException("LocalAccounts:Enabled requires Frontend:Enabled.");
}
if (workerOptions.DeliveryEnabled && !keyManagementEnabled)
{
    throw new InvalidOperationException("Delivery workers require KeyManagement:Enabled.");
}

if (isProduction && !keyManagementEnabled)
{
    throw new InvalidOperationException("KeyManagement:Enabled must be true in Production.");
}

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Math.Max(federationOptions.MaximumInboxBodyBytes, mediaOptions.MaximumUploadBytes));
builder.Services.AddProblemDetails();
if (hostFrontend)
{
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DetailedErrors = false;
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2);
            options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(15);
            options.MaxBufferedUnacknowledgedRenderBatches = 10;
        })
        .AddHubOptions(options =>
        {
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.MaximumParallelInvocationsPerClient = 1;
            options.MaximumReceiveMessageSize = 64 * 1024;
        });
    builder.Services.AddMisskeyBlazorFrontend(new MisskeyFrontendRuntimeConfiguration(
        MisskeyFrontendRuntimeConfiguration.PortVersion,
        frontendOptions.SourceUrl,
        frontendOptions.PublicBaseUri,
        localAccountOptions.Enabled));
}

builder.Services.AddSingleton(misskeyAuthenticationOptions);
builder.Services.AddSingleton(localAccountOptions);
builder.Services.AddSingleton(registrationProtectionOptions);
builder.Services.AddSingleton(passwordResetOptions);
builder.Services.AddSingleton(new MisskeyRegistrationPolicy(
    registrationProtectionOptions.RegistrationAvailable(localAccountOptions),
    localAccountOptions.RequireConfirmedEmail,
    passwordResetOptions.Enabled,
    OpenRegistration: localAccountOptions.RegistrationEnabled,
    InvitationRequired: registrationProtectionOptions.InvitationRequired,
    CaptchaProvider: registrationProtectionOptions.CaptchaProvider.ToString(),
    CaptchaSiteKey: registrationProtectionOptions.CaptchaSiteKey,
    TurnstileAction: registrationProtectionOptions.CaptchaExpectedAction,
    TurnstileCdata: registrationProtectionOptions.CaptchaExpectedCdata));
builder.Services.AddSingleton(streamingOptions);
builder.Services.AddSingleton(new StreamingRuntimeIdentity($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"));
builder.Services.AddActivityPubPersistence(builder.Configuration, localAccountOptions.Enabled);
builder.Services.AddActivityPubDataProtection(builder.Configuration, isProduction);
builder.Services.AddActivityPubFederation(federationOptions, isProduction, keyManagementEnabled);
builder.Services.AddActivityPubApiAuthentication(
    authenticationOptions,
    oauthOptions.Enabled,
    isProduction,
    context => context.GetEndpoint()?.Metadata.GetMetadata<FrontendPathBaseRequiredMetadata>() is not null);
if (oauthOptions.Enabled)
{
    builder.Services.AddActivityPubOAuthAuthorizationServer<FederationDbContext>(
        oauthOptions,
        authenticationOptions,
        federationOptions.PublicBaseUri,
        frontendOptions.Authority,
        frontendOptions.PublicBaseUri,
        isProduction);
}
if (localAccountOptions.Enabled)
{
    builder.Services.AddActivityPubLocalAccounts<LocalIdentityDbContext>(
        localAccountOptions,
        frontendOptions.PublicBaseUri,
        registrationProtectionOptions);
    builder.Services.AddActivityPubPasswordReset(passwordResetOptions);
}
builder.Services.AddActivityPubMedia(mediaOptions, isProduction);
builder.Services.AddScoped<MastodonQueryService>();
builder.Services.AddScoped<MastodonCommandService>();
builder.Services.AddScoped<MisskeyReactionService>();
builder.Services.AddScoped<MisskeyQueryService>();
builder.Services.AddScoped<MisskeyCommandService>();
builder.Services.AddScoped<MisskeyAnnouncementService>();
builder.Services.AddScoped<IRelayCommandService, RelayService>();
builder.Services.AddScoped<MisskeyMetadataService>();
builder.Services.AddActivityPubModeration(spamOptions);
builder.Services.AddActivityPubWorkers(workerOptions);
builder.Services.AddActivityPubOperations(workerOptions, typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");

if (keyManagementEnabled)
{
    VaultTransitOptions vaultOptions = ConfigurationReader.ReadVaultTransit(builder.Configuration);
    builder.Services.AddExternalKeySigning(vaultOptions, isProduction);
    builder.Services.AddLocalActorAdministration();
}

string[] corsOrigins = builder.Configuration.GetSection("Http:AllowedCorsOrigins").Get<string[]>() ?? [];
if (isProduction && corsOrigins.Length == 0)
{
    throw new InvalidOperationException("Http:AllowedCorsOrigins must be explicitly configured in Production.");
}

builder.Services.AddCors(options => options.AddPolicy("local-api", policy =>
{
    policy.WithOrigins(corsOrigins).WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Authorization", "Content-Type", "Idempotency-Key");
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        ActivityPub.Operations.FederationTelemetry.RateLimited.Add(1);
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("inbox", context => RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            TokensPerPeriod = 60,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("signed-get", context => FixedWindow(context, 120));
    options.AddPolicy("federation-get", context => FixedWindow(context, 300));
    options.AddPolicy("discovery", context => FixedWindow(context, 180));
    options.AddPolicy("local-api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("misskey-signin", context => RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("initial-setup", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-reset-request", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-reset-complete", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    ForwardLimit = 1
};
TrustedProxySet trustedProxies = TrustedProxySet.Read(builder.Configuration);
trustedProxies.ApplyTo(forwarded);
bool cloudflareProxyEnabled = builder.Configuration.GetValue("Http:Cloudflare:Enabled", false);
if (cloudflareProxyEnabled)
{
    forwarded.ForwardedForHeaderName = CloudflareConnectingIpGuard.HeaderName;
}

if ((isProduction || cloudflareProxyEnabled) && trustedProxies.Count == 0)
{
    throw new InvalidOperationException(
        "Http:TrustedProxies or Http:TrustedProxyNetworks must explicitly identify the TLS-terminating proxy.");
}

string[] allowedRequestHosts = hostFrontend &&
    !string.Equals(federationOptions.PublicBaseUri.IdnHost, frontendOptions.PublicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
        ? [federationOptions.PublicBaseUri.IdnHost, frontendOptions.PublicBaseUri.IdnHost]
        : [federationOptions.PublicBaseUri.IdnHost];
builder.Services.Configure<HostFilteringOptions>(options => options.AllowedHosts = allowedRequestHosts);

WebApplication app = builder.Build();
if (!isProduction && (!federationOptions.RequireHttps || federationOptions.AllowDevelopmentLoopback ||
    federationOptions.DevelopmentRestrictToAllowedHosts ||
    federationOptions.DevelopmentAllowedHosts.Length > 0))
{
    StartupLog.DevelopmentFederationNetworkExceptions(
        app.Logger,
        federationOptions.RequireHttps,
        federationOptions.AllowDevelopmentLoopback,
        federationOptions.DevelopmentRestrictToAllowedHosts,
        string.Join(',', federationOptions.DevelopmentAllowedHosts));
}

if (args is ["migrate"])
{
    await MigrationCommand.RunAsync(app.Services, CancellationToken.None);
    return;
}

if (args is ["dependency-probe", .. var probeArguments])
{
    await DependencyProbeCommand.RunAsync(app.Services, probeArguments, CancellationToken.None);
    return;
}

if (args is ["create-local-actor", .. var actorArguments])
{
    await LocalActorCommand.RunAsync(app.Services, actorArguments, CancellationToken.None);
    return;
}

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(ExceptionResponse.WriteAsync));
if (cloudflareProxyEnabled)
{
    // Validate the direct peer before ForwardedHeaders replaces RemoteIpAddress. This
    // prevents a direct-origin caller from granting itself a spoofed CF-Connecting-IP.
    app.UseMiddleware<CloudflareConnectingIpGuard>(trustedProxies);
}
app.UseForwardedHeaders(forwarded);
app.UseHostFiltering();
app.UseMiddleware<StreamingTokenRedactionMiddleware>();
if (isProduction)
{
    app.UseHsts();
}

if (federationOptions.RequireHttps)
{
    app.UseHttpsRedirection();
}
if (hostFrontend)
{
    app.UseMisskeyFrontendLocalization();
}
app.UseFrontendAssets(frontendOptions, registrationProtectionOptions);

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = streamingOptions.HeartbeatInterval,
    KeepAliveTimeout = streamingOptions.HeartbeatInterval
};
var webSocketOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
AddWebSocketOrigin(webSocketOrigins, federationOptions.PublicBaseUri);
if (hostFrontend)
{
    AddWebSocketOrigin(webSocketOrigins, frontendOptions.PublicBaseUri);
}

foreach (string configuredOrigin in corsOrigins)
{
    bool hasHttpScheme = Uri.TryCreate(configuredOrigin, UriKind.Absolute, out Uri? origin) &&
        (string.Equals(origin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    if (!hasHttpScheme || origin is null ||
        !string.IsNullOrEmpty(origin.UserInfo) ||
        !string.Equals(configuredOrigin.TrimEnd('/'), origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Http:AllowedCorsOrigins contains an invalid origin: '{configuredOrigin}'.");
    }

    AddWebSocketOrigin(webSocketOrigins, origin);
}

app.UseWebSockets(webSocketOptions);
app.Use(async (context, next) =>
{
    if (context.WebSockets.IsWebSocketRequest &&
        context.Request.Headers.TryGetValue("Origin", out Microsoft.Extensions.Primitives.StringValues originValues) &&
        !IsAllowedWebSocketOrigin(originValues, webSocketOrigins))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next(context).ConfigureAwait(false);
});
app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<FrontendPathBaseRequiredMetadata>() is not null &&
        context.Request.PathBase.HasValue)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    bool boundedAuthenticationForm = context.GetEndpoint()?.Metadata
        .GetMetadata<BoundedAuthenticationFormMetadata>() is not null;
    try
    {
        await next(context).ConfigureAwait(false);
    }
    catch (InvalidDataException exception) when (boundedAuthenticationForm)
    {
        throw new BadHttpRequestException(
            "Request form exceeds the authentication limits.",
            StatusCodes.Status413PayloadTooLarge,
            exception);
    }
});
app.UseRateLimiter();
app.UseCors("local-api");
app.UseAuthentication();
app.UseAuthorization();
if (hostFrontend)
{
    app.UseAntiforgery();
}

app.MapStaticAssets();
app.MapActivityPubHealthEndpoints();
if (oauthOptions.Enabled)
{
    app.MapActivityPubOAuthEndpoints();
}
app.MapFederationEndpoints();
app.MapMastodonApi(oauthOptions.Enabled);
app.MapMisskeyApi();
app.MapFrontendEndpoints(
    frontendOptions,
    localAccountOptions,
    registrationProtectionOptions,
    passwordResetOptions,
    builder.Environment.IsDevelopment());
if (hostFrontend)
{
    RazorComponentsEndpointConventionBuilder components = app
        .MapRazorComponents<ActivityPub.Misskey.Blazor.App>()
        .AddInteractiveServerRenderMode();
    components.Add(builder => builder.Metadata.Add(FrontendPathBaseRequiredMetadata.Instance));
}

app.MapAdminEndpoints(keyManagementEnabled);
app.MapMediaEndpoints(mediaOptions);
app.Run();

static RateLimitPartition<string> FixedWindow(HttpContext context, int limit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

static void AddWebSocketOrigin(HashSet<string> origins, Uri value)
{
    origins.Add(value.GetLeftPart(UriPartial.Authority));
}

static bool IsAllowedWebSocketOrigin(
    Microsoft.Extensions.Primitives.StringValues values,
    HashSet<string> allowedOrigins)
{
    if (values.Count != 1 ||
        !Uri.TryCreate(values[0], UriKind.Absolute, out Uri? value) ||
        !string.IsNullOrEmpty(value.UserInfo) ||
        value.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(value.Query) ||
        !string.IsNullOrEmpty(value.Fragment))
    {
        return false;
    }

    return allowedOrigins.Contains(value.GetLeftPart(UriPartial.Authority));
}

public partial class Program;
