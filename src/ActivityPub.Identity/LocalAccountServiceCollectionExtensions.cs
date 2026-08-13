using System.Net;
using System.Security.Claims;
using ActivityPub.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Identity;

public static class LocalAccountServiceCollectionExtensions
{
    public const string LocalIdentityClaim = "activitypub.local_identity_user_id";
    public const string LocalActorClaim = "activitypub.local_actor_iri";

    public static IServiceCollection AddActivityPubLocalAccounts<TContext>(
        this IServiceCollection services,
        LocalAccountOptions options,
        Uri? publicBaseUri = null,
        RegistrationProtectionOptions? protectionOptions = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        protectionOptions ??= new RegistrationProtectionOptions();

        services.TryAddSingleton(options);
        services.TryAddSingleton(protectionOptions);
        services.AddIdentityCore<LocalIdentityUser>(identity =>
            {
                identity.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
                // Optional email addresses are valid for closed/local-account deployments.
                // PostgreSQL enforces uniqueness for non-null normalized addresses with a
                // filtered unique index, avoiding Identity's RequireUniqueEmail null rejection.
                identity.User.RequireUniqueEmail = false;
                identity.Password.RequiredLength = options.RequiredPasswordLength;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredUniqueChars = 1;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = options.MaximumFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = options.LockoutDuration;
                identity.SignIn.RequireConfirmedEmail = options.RequireConfirmedEmail;
                identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<LocalIdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddEntityFrameworkStores<TContext>();
        services.Configure<IdentityPasskeyOptions>(passkeys =>
        {
            // Never derive the RP ID from an untrusted Host header in this application.
            // Production passes the immutable configured PublicBaseUri explicitly.
            passkeys.ServerDomain = publicBaseUri?.IdnHost;
            passkeys.AuthenticatorTimeout = TimeSpan.FromMinutes(1);
            passkeys.UserVerificationRequirement = "required";
        });
        services.AddAuthentication()
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, cookie =>
            {
                cookie.Cookie.Name = "__Host-activitypub-passkey-state";
                cookie.Cookie.Path = "/";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(2);
                cookie.SlidingExpiration = false;
            });
        services.AddScoped<LocalAccountService>();
        services.TryAddSingleton<IPasswordVerificationTimingEqualizer, PasswordVerificationTimingEqualizer>();
        services.AddScoped<ILocalAccountService>(provider => provider.GetRequiredService<LocalAccountService>());
        services.AddScoped<IRegistrationAvailabilityService>(provider => provider.GetRequiredService<LocalAccountService>());
        services.AddScoped<IRegistrationProtectionService, RegistrationProtectionService>();
        services.AddScoped<IRegistrationInvitationService, RegistrationInvitationService>();
        services.AddHttpClient<IRegistrationCaptchaVerifier, RegistrationCaptchaVerifier>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ActivityPub.NET/registration-captcha");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                UseCookies = false
            });
        services.AddScoped<ILocalAccountPrincipalFactory, LocalAccountPrincipalFactory>();
        services.AddScoped<LocalAccountCookieEvents>();
        services.Configure<CookieAuthenticationOptions>(
            OAuthAuthorizationServerExtensions.ExternalSessionScheme,
            cookie =>
            {
                cookie.EventsType = typeof(LocalAccountCookieEvents);
                cookie.ExpireTimeSpan = options.SessionLifetime;
                cookie.SlidingExpiration = false;
            });
        return services;
    }
}

public interface ILocalAccountPrincipalFactory
{
    Task<ClaimsPrincipal> CreateAsync(LocalIdentityUser user);
}

internal sealed class LocalAccountPrincipalFactory(
    IUserClaimsPrincipalFactory<LocalIdentityUser> inner) : ILocalAccountPrincipalFactory
{
    public async Task<ClaimsPrincipal> CreateAsync(LocalIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ClaimsPrincipal principal = await inner.CreateAsync(user).ConfigureAwait(false);
        if (principal.Identity is not ClaimsIdentity identity)
        {
            throw new InvalidOperationException("ASP.NET Core Identity returned no writable claims identity.");
        }

        string username = user.UserName ?? throw new InvalidOperationException("An activated local account has no username.");
        identity.AddClaim(new Claim(LocalAccountServiceCollectionExtensions.LocalIdentityClaim, user.Id.ToString("N")));
        identity.AddClaim(new Claim("sub", user.Id.ToString("N")));
        identity.AddClaim(new Claim("preferred_username", username));
        identity.AddClaim(new Claim("scope", "openid profile activitypub.read activitypub.write"));
        identity.AddClaim(new Claim(FrontendBrowserSessionMetadata.SessionClaim, "true"));
        if (!string.IsNullOrWhiteSpace(user.LocalActorIri))
        {
            identity.AddClaim(new Claim(LocalAccountServiceCollectionExtensions.LocalActorClaim, user.LocalActorIri));
        }

        return principal;
    }
}

internal sealed class LocalAccountCookieEvents(
    UserManager<LocalIdentityUser> users,
    LocalAccountOptions options) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        string? value = context.Principal?.FindFirst(LocalAccountServiceCollectionExtensions.LocalIdentityClaim)?.Value;
        if (value is null)
        {
            await base.ValidatePrincipal(context).ConfigureAwait(false);
            return;
        }

        if (!options.Enabled || !Guid.TryParseExact(value, "N", out Guid userId))
        {
            context.RejectPrincipal();
            return;
        }

        LocalIdentityUser? user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        string? principalStamp = context.Principal?.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value;
        string? storedStamp = user is null ? null : await users.GetSecurityStampAsync(user).ConfigureAwait(false);
        if (user is null || user.ProvisioningState != LocalAccountProvisioningState.Active ||
            string.IsNullOrWhiteSpace(principalStamp) ||
            !string.Equals(principalStamp, storedStamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            return;
        }

        await base.ValidatePrincipal(context).ConfigureAwait(false);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (FrontendBrowserSessionMetadata.IsExplicitBrowserRequest(context.HttpContext))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }

        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (FrontendBrowserSessionMetadata.IsExplicitBrowserRequest(context.HttpContext))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }

        return base.RedirectToAccessDenied(context);
    }
}
