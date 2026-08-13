using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenIddict.Validation.AspNetCore;

namespace ActivityPub.Identity;

public static class OAuthAuthorizationServerExtensions
{
    public const string ExternalSessionScheme = "activitypub.external.session";

    public static IServiceCollection AddActivityPubFrontendBrowserSession(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = FrontendBrowserSessionMetadata.AntiforgeryHeaderName;
            antiforgery.Cookie.Name = "__Host-activitypub-oauth-csrf";
            antiforgery.Cookie.Path = "/";
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        });
        services.AddAuthentication()
            .AddCookie(ExternalSessionScheme, cookie =>
            {
                cookie.Cookie.Name = "__Host-activitypub-oauth-session";
                cookie.Cookie.Path = "/";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                cookie.SlidingExpiration = false;
            });
        return services;
    }

    public static IServiceCollection AddActivityPubOAuthAuthorizationServer<TContext>(
        this IServiceCollection services,
        OAuthAuthorizationServerOptions options,
        ApiAuthenticationOptions authentication,
        Uri publicBaseUri,
        Uri interactiveAuthority,
        Uri interactivePublicBaseUri,
        bool isProduction)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(publicBaseUri);
        ArgumentNullException.ThrowIfNull(interactiveAuthority);
        ArgumentNullException.ThrowIfNull(interactivePublicBaseUri);
        options.Validate(isProduction);

        Uri interactiveCallbackUri = new(interactivePublicBaseUri, options.CallbackPath);
        Uri interactiveLogoutUri = new(interactivePublicBaseUri, "/");
        string metadataAddress = authentication.Authority.AbsoluteUri.TrimEnd('/') +
            "/.well-known/openid-configuration";

        services.AddSingleton(options);
        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<TContext>())
            .AddServer(server =>
            {
                server.SetIssuer(new Uri(publicBaseUri.AbsoluteUri.TrimEnd('/') + '/', UriKind.Absolute));
                server.SetAuthorizationEndpointUris("/oauth/authorize");
                server.SetTokenEndpointUris("/oauth/token");
                server.SetRevocationEndpointUris("/oauth/revoke");
                server.AllowAuthorizationCodeFlow();
                server.AllowClientCredentialsFlow();
                server.AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes(MastodonOAuthScopes.All.ToArray());
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                server.SetRefreshTokenLifetime(options.RefreshTokenLifetime);
                server.SetRefreshTokenReuseLeeway(options.RefreshTokenReuseLeeway);
                server.UseReferenceAccessTokens();
                server.UseReferenceRefreshTokens();
                if (isProduction)
                {
                    server.AddSigningCertificate(LoadCertificate(
                        options.SigningCertificatePath!,
                        options.SigningCertificatePasswordFile!));
                    server.AddEncryptionCertificate(LoadCertificate(
                        options.EncryptionCertificatePath!,
                        options.EncryptionCertificatePasswordFile!));
                }
                else
                {
                    server.AddEphemeralSigningKey();
                    server.AddEphemeralEncryptionKey();
                }

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
                validation.EnableTokenEntryValidation();
            });

        services.AddScoped<IMastodonOAuthApplicationService, MastodonOAuthApplicationService>();
        return services;
    }

    private static X509Certificate2 LoadCertificate(string path, string passwordFile)
    {
        if (!File.Exists(path) || !File.Exists(passwordFile))
        {
            throw new InvalidOperationException("An OAuth certificate or its password file does not exist.");
        }

        string password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    private static string? ReadOptionalSecret(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("OAuth:InteractiveClientSecretFile must refer to an existing absolute path.");
        }

        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }
}
