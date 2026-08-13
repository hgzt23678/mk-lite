using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace ActivityPub.Identity;

public static class ApiAuthenticationExtensions
{
    public const string CombinedBearerScheme = "activitypub.bearer";

    public static IServiceCollection AddActivityPubApiAuthentication(
        this IServiceCollection services,
        ApiAuthenticationOptions options,
        bool localOAuthEnabled,
        bool frontendBrowserSessionEnabled,
        bool isProduction,
        Func<HttpContext, bool>? isFrontendPage = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(isProduction);

        services.AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme = CombinedBearerScheme;
                authentication.DefaultChallengeScheme = CombinedBearerScheme;
                authentication.DefaultForbidScheme = CombinedBearerScheme;
            })
            .AddPolicyScheme(CombinedBearerScheme, CombinedBearerScheme, policy =>
            {
                policy.ForwardDefaultSelector = context =>
                {
                    string authorization = context.Request.Headers.Authorization.ToString();
                    if (frontendBrowserSessionEnabled &&
                        ((isFrontendPage?.Invoke(context) ?? false) ||
                         string.IsNullOrEmpty(authorization) &&
                         (FrontendBrowserSessionMetadata.IsExplicitBrowserRequest(context) ||
                          FrontendBrowserSessionMetadata.IsBrowserWebSocketRequest(context))))
                    {
                        return OAuthAuthorizationServerExtensions.ExternalSessionScheme;
                    }

                    if (authorization.StartsWith("Bearer mk_", StringComparison.Ordinal) ||
                        string.IsNullOrEmpty(authorization) &&
                        (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/streaming")))
                    {
                        return MisskeyTokenAuthenticationHandler.SchemeName;
                    }

                    if (localOAuthEnabled && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        string token = authorization["Bearer ".Length..].Trim();
                        if (token.Count(character => character == '.') != 2)
                        {
                            return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                        }
                    }

                    return JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, MisskeyTokenAuthenticationHandler>(
                MisskeyTokenAuthenticationHandler.SchemeName,
                _ => { })
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority.AbsoluteUri;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = "preferred_username";
                jwt.TokenValidationParameters.RoleClaimType = "role";
                jwt.SaveToken = false;
            });
        services.AddAuthorizationBuilder()
            .AddPolicy("activitypub.read", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !IsMisskeyToken(context.User) &&
                    (HasScope(context.User, "activitypub.read") || HasScope(context.User, "activitypub.write"))))
            .AddPolicy("activitypub.write", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !IsMisskeyToken(context.User) && HasScope(context.User, "activitypub.write")))
            .AddPolicy("mastodon.read", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsMisskeyToken(context.User) ||
                    HasScope(context.User, "read") ||
                    HasScope(context.User, "read:accounts") ||
                    HasScope(context.User, "activitypub.read") ||
                    HasScope(context.User, "activitypub.write")))
            .AddPolicy("mastodon.write", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsMisskeyToken(context.User)
                        ? context.User.HasClaim("misskey.permission", "write:notes")
                        : HasScope(context.User, "write") ||
                          HasScope(context.User, "write:statuses") ||
                          HasScope(context.User, "activitypub.write")))
            .AddPolicy("mastodon.read:notifications", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsMisskeyToken(context.User)
                        ? context.User.HasClaim("misskey.permission", "read:notifications")
                        : HasScope(context.User, "read") ||
                          HasScope(context.User, "read:notifications") ||
                          HasScope(context.User, "activitypub.read") ||
                          HasScope(context.User, "activitypub.write")))
            .AddPolicy("mastodon.write:notifications", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsMisskeyToken(context.User)
                        ? context.User.HasClaim("misskey.permission", "write:notifications")
                        : HasScope(context.User, "write") ||
                          HasScope(context.User, "write:notifications") ||
                          HasScope(context.User, "activitypub.write")))
            .AddPolicy("mastodon.read:follows", policy => MastodonScopePolicy(policy, "read:follows", "read:following", write: false))
            .AddPolicy("mastodon.write:favourites", policy => MastodonScopePolicy(policy, "write:favourites", "write:reactions", write: true))
            .AddPolicy("mastodon.write:follows", policy => MastodonScopePolicy(policy, "write:follows", "write:following", write: true))
            .AddPolicy("mastodon.write:mutes", policy => MastodonScopePolicy(policy, "write:mutes", "write:mutes", write: true))
            .AddPolicy("mastodon.write:blocks", policy => MastodonScopePolicy(policy, "write:blocks", "write:blocks", write: true))
            .AddPolicy("misskey.read", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsFrontendSession(context.User) ||
                    HasScope(context.User, "activitypub.read") ||
                    HasScope(context.User, "activitypub.write") ||
                    context.User.HasClaim(claim => claim.Type == "misskey.permission" &&
                        (claim.Value.StartsWith("read:", StringComparison.Ordinal) ||
                         claim.Value.StartsWith("write:", StringComparison.Ordinal)))))
            .AddPolicy("misskey.write", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    IsFrontendSession(context.User) ||
                    HasScope(context.User, "activitypub.write") ||
                    context.User.HasClaim(claim => claim.Type == "misskey.permission" &&
                        claim.Value.StartsWith("write:", StringComparison.Ordinal))))
            .AddPolicy("misskey.write:account", policy => MisskeyPermissionPolicy(policy, "write:account"))
            .AddPolicy("misskey.read:drive", policy => MisskeyReadPermissionPolicy(policy, "read:drive"))
            .AddPolicy("misskey.write:drive", policy => MisskeyPermissionPolicy(policy, "write:drive"))
            .AddPolicy("misskey.secure", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.Identities.All(identity =>
                    !string.Equals(identity.AuthenticationType, MisskeyTokenAuthenticationHandler.SchemeName, StringComparison.Ordinal))))
            .AddPolicy("misskey.write:notes", policy => MisskeyPermissionPolicy(policy, "write:notes"))
            .AddPolicy("misskey.write:reactions", policy => MisskeyPermissionPolicy(policy, "write:reactions"))
            .AddPolicy("misskey.write:votes", policy => MisskeyPermissionPolicy(policy, "write:votes"))
            .AddPolicy("misskey.read:notifications", policy => MisskeyReadPermissionPolicy(policy, "read:notifications"))
            .AddPolicy("misskey.write:notifications", policy => MisskeyPermissionPolicy(policy, "write:notifications"))
            .AddPolicy("misskey.read:following", policy => MisskeyReadPermissionPolicy(policy, "read:following"))
            .AddPolicy("misskey.write:following", policy => MisskeyPermissionPolicy(policy, "write:following"))
            .AddPolicy("misskey.write:mutes", policy => MisskeyPermissionPolicy(policy, "write:mutes"))
            .AddPolicy("misskey.write:blocks", policy => MisskeyPermissionPolicy(policy, "write:blocks"))
            .AddPolicy("activitypub.admin", policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("activitypub-admin")
                .RequireAssertion(context =>
                    IsFrontendSession(context.User) || HasScope(context.User, "activitypub.admin")));
        return services;
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope) =>
        principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

    private static void MastodonScopePolicy(
        AuthorizationPolicyBuilder policy,
        string scope,
        string misskeyPermission,
        bool write) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            IsMisskeyToken(context.User)
                ? context.User.HasClaim("misskey.permission", misskeyPermission)
                : HasScope(context.User, write ? "write" : "read") ||
                  HasScope(context.User, scope) ||
                  HasScope(context.User, write ? "activitypub.write" : "activitypub.read") ||
                  !write && HasScope(context.User, "activitypub.write"));

    private static void MisskeyPermissionPolicy(AuthorizationPolicyBuilder policy, string permission) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            IsFrontendSession(context.User) ||
            context.User.HasClaim("misskey.permission", permission) ||
            !IsMisskeyToken(context.User) && HasScope(context.User, "activitypub.write"));

    private static void MisskeyReadPermissionPolicy(AuthorizationPolicyBuilder policy, string permission) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            IsFrontendSession(context.User) ||
            context.User.HasClaim("misskey.permission", permission) ||
            !IsMisskeyToken(context.User) &&
            (HasScope(context.User, "activitypub.read") || HasScope(context.User, "activitypub.write")));

    private static bool IsMisskeyToken(ClaimsPrincipal principal) => principal.Identities.Any(identity =>
        identity.IsAuthenticated &&
        string.Equals(
            identity.AuthenticationType,
            MisskeyTokenAuthenticationHandler.SchemeName,
            StringComparison.Ordinal));

    private static bool IsFrontendSession(ClaimsPrincipal principal) =>
        principal.HasClaim(FrontendBrowserSessionMetadata.SessionClaim, "true");
}
