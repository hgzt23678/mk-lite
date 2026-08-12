using System.Collections.Immutable;
using System.Security.Cryptography;
using ActivityPub.Application;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActivityPub.Identity;

public sealed record MastodonOAuthApplicationRegistration(
    string Name,
    string? Website,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> Scopes);

public sealed record MastodonOAuthApplicationCredentials(
    Guid InternalId,
    string ClientId,
    string ClientSecret,
    string Name,
    string? Website,
    IReadOnlyList<string> RedirectUris);

public sealed record MastodonOAuthApplication(
    Guid InternalId,
    string ClientId,
    string Name,
    string? Website,
    IReadOnlyList<string> RedirectUris);

public interface IMastodonOAuthApplicationService
{
    Task<MastodonOAuthApplicationCredentials> RegisterAsync(
        MastodonOAuthApplicationRegistration registration,
        CancellationToken cancellationToken);

    Task<MastodonOAuthApplication?> FindAsync(
        string clientId,
        CancellationToken cancellationToken);
}

public sealed class MastodonOAuthApplicationService(
    IOpenIddictApplicationManager applications,
    IAuditLog audit,
    ILogger<MastodonOAuthApplicationService> logger) : IMastodonOAuthApplicationService
{
    public async Task<MastodonOAuthApplicationCredentials> RegisterAsync(
        MastodonOAuthApplicationRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        string name = Required(registration.Name, nameof(registration.Name), 200);
        string? website = OptionalAbsoluteUri(registration.Website, nameof(registration.Website));
        Uri[] redirectUris = registration.RedirectUris
            .Select(ParseRedirectUri)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
        if (redirectUris.Length is 0 or > 20)
        {
            throw new ArgumentException("Between one and twenty redirect URIs are required.", nameof(registration));
        }

        string[] scopes = registration.Scopes.Count == 0
            ? ["read"]
            : registration.Scopes.Distinct(StringComparer.Ordinal).ToArray();
        string? unsupported = scopes.FirstOrDefault(scope => !MastodonOAuthScopes.All.Contains(scope));
        if (unsupported is not null)
        {
            throw new ArgumentException($"Unsupported OAuth scope: {unsupported}", nameof(registration));
        }

        string clientId = RandomToken(24);
        string clientSecret = RandomToken(48);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ApplicationType = ApplicationTypes.Web,
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = name
        };
        foreach (Uri redirectUri in redirectUris)
        {
            descriptor.RedirectUris.Add(redirectUri);
        }

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        foreach (string scope in scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        if (website is not null)
        {
            descriptor.Properties["website"] = System.Text.Json.JsonSerializer.SerializeToElement(website);
        }
        object application = await applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        string? applicationId = await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParse(applicationId, out Guid internalId))
        {
            throw new InvalidOperationException("The OAuth application store returned a non-UUID identifier.");
        }

        await audit.AppendAsync(
            "oauth",
            "application-registered",
            "anonymous-client",
            clientId,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                name,
                website,
                redirectUris = redirectUris.Select(uri => uri.AbsoluteUri),
                scopes
            }),
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        OAuthLog.ApplicationRegistered(logger, clientId, name);
        return new(internalId, clientId, clientSecret, name, website, redirectUris.Select(uri => uri.AbsoluteUri).ToArray());
    }

    public async Task<MastodonOAuthApplication?> FindAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        object? application = await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        string? applicationId = await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParse(applicationId, out Guid internalId))
        {
            throw new InvalidOperationException("The OAuth application store returned a non-UUID identifier.");
        }

        string name = await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The OAuth application has no display name.");
        ImmutableDictionary<string, System.Text.Json.JsonElement> properties =
            await applications.GetPropertiesAsync(application, cancellationToken).ConfigureAwait(false);
        string? website = properties.TryGetValue("website", out System.Text.Json.JsonElement value) &&
            value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
        ImmutableArray<string> redirects = await applications.GetRedirectUrisAsync(application, cancellationToken).ConfigureAwait(false);
        return new(
            internalId,
            clientId,
            name,
            website,
            redirects.ToArray());
    }

    private static Uri ParseRedirectUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("OAuth redirect URIs must be absolute and cannot contain a fragment.", nameof(value));
        }

        bool oob = string.Equals(uri.AbsoluteUri, "urn:ietf:wg:oauth:2.0:oob", StringComparison.Ordinal);
        bool https = uri.Scheme == Uri.UriSchemeHttps;
        bool loopbackHttp = uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback ||
            string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(uri.Host, "[::1]", StringComparison.Ordinal));
        bool privateUseScheme = !uri.Scheme.Contains('.', StringComparison.Ordinal) &&
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeFile;
        if (!oob && !https && !loopbackHttp && !privateUseScheme)
        {
            throw new ArgumentException("OAuth redirect URIs must use HTTPS, a loopback HTTP URI, a private-use scheme, or the OOB URN.", nameof(value));
        }

        return uri;
    }

    private static string Required(string value, string name, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        string normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"{name} exceeds {maximumLength} characters.", name);
    }

    private static string? OptionalAbsoluteUri(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : throw new ArgumentException($"{name} must be an absolute HTTP(S) URI.", name);
    }

    private static string RandomToken(int bytes) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
}

public static class MastodonOAuthScopes
{
    public static ImmutableHashSet<string> All { get; } = new[]
    {
        "read", "read:accounts", "read:blocks", "read:bookmarks", "read:favourites", "read:filters",
        "read:follows", "read:lists", "read:mutes", "read:notifications", "read:search", "read:statuses",
        "write", "write:accounts", "write:blocks", "write:bookmarks", "write:conversations", "write:favourites",
        "write:filters", "write:follows", "write:lists", "write:media", "write:mutes", "write:notifications",
        "write:reports", "write:statuses", "follow", "push", "offline_access", "admin:read", "admin:read:accounts",
        "admin:read:reports", "admin:read:domain_allows", "admin:read:domain_blocks", "admin:read:ip_blocks",
        "admin:read:email_domain_blocks", "admin:read:canonical_email_blocks", "admin:write",
        "admin:write:accounts", "admin:write:reports", "admin:write:domain_allows", "admin:write:domain_blocks",
        "admin:write:ip_blocks", "admin:write:email_domain_blocks", "admin:write:canonical_email_blocks"
    }.ToImmutableHashSet(StringComparer.Ordinal);
}

internal static partial class OAuthLog
{
    [LoggerMessage(LogLevel.Information, "Registered OAuth application {ClientId} ({Name})")]
    public static partial void ApplicationRegistered(ILogger logger, string clientId, string name);
}
