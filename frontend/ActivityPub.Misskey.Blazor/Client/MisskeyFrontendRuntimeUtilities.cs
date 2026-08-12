using System.Text.Json;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyFrontendCapabilities(
    bool PublicTimeline,
    bool LocalTimeline,
    bool HomeTimeline,
    bool Compose,
    bool Favourite,
    bool Renote,
    bool Mute,
    bool MediaUpload,
    bool Notifications,
    bool Streaming);

public sealed record MisskeyFrontendRuntimeConfig(
    bool Enabled,
    string InstanceName,
    Uri Authority,
    string ClientId,
    IReadOnlyList<string> Scopes,
    Uri RedirectUri,
    Uri PostLogoutRedirectUri,
    Uri? SourceUrl,
    MisskeyFrontendCapabilities Capabilities);

/// <summary>Fail-closed validation for the pinned Vue bootstrap contract.</summary>
public static class MisskeyFrontendRuntimeUtilities
{
    private static readonly string[] CapabilityNames =
    [
        "publicTimeline", "localTimeline", "homeTimeline", "compose", "favourite",
        "renote", "mute", "mediaUpload", "notifications", "streaming",
    ];

    public static MisskeyFrontendRuntimeConfig Validate(JsonElement value, Uri currentOrigin)
    {
        ArgumentNullException.ThrowIfNull(currentOrigin);
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("enabled", out JsonElement enabled) ||
            (enabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False) ||
            !value.TryGetProperty("capabilities", out JsonElement capabilities) ||
            capabilities.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Invalid frontend configuration.", nameof(value));
        }

        Uri authority = ReadUri(value, "authority", requireHttps: true);
        Uri redirect = ReadUri(value, "redirectUri", requireHttps: false);
        Uri postLogout = ReadUri(value, "postLogoutRedirectUri", requireHttps: false);
        if (!Uri.Compare(redirect, currentOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase).Equals(0) ||
            !Uri.Compare(postLogout, currentOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase).Equals(0) ||
            redirect.AbsolutePath != "/app/auth/callback" || postLogout.AbsolutePath != "/app/")
        {
            throw new ArgumentException("Unsafe OIDC frontend configuration.", nameof(value));
        }

        if (!value.TryGetProperty("scopes", out JsonElement scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Invalid OAuth scopes.", nameof(value));
        }

        List<string> scopeValues = [];
        foreach (JsonElement scope in scopes.EnumerateArray())
        {
            if (scope.ValueKind != JsonValueKind.String || !TryReadString(scope, out string? text) || string.IsNullOrEmpty(text) || text.Length > 128)
            {
                throw new ArgumentException("Invalid OAuth scopes.", nameof(value));
            }

            scopeValues.Add(text);
        }

        if (scopeValues.Count == 0)
        {
            throw new ArgumentException("Invalid OAuth scopes.", nameof(value));
        }

        bool[] capabilityValues = CapabilityNames.Select(name => ReadBoolean(capabilities, name)).ToArray();
        Uri? source = null;
        if (value.TryGetProperty("sourceUrl", out JsonElement sourceValue) && sourceValue.ValueKind != JsonValueKind.Null)
        {
            source = ReadUri(value, "sourceUrl", requireHttps: true);
        }

        return new(
            enabled.GetBoolean(),
            ReadString(value, "instanceName"),
            RemoveTrailingSlash(authority),
            ReadString(value, "clientId"),
            scopeValues,
            redirect,
            postLogout,
            source,
            new(
                capabilityValues[0], capabilityValues[1], capabilityValues[2], capabilityValues[3], capabilityValues[4],
                capabilityValues[5], capabilityValues[6], capabilityValues[7], capabilityValues[8], capabilityValues[9]));
    }

    private static Uri ReadUri(JsonElement value, string property, bool requireHttps)
    {
        string text = ReadString(value, property);
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
            (requireHttps && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Invalid URI field: {property}.", nameof(value));
        }

        return uri;
    }

    private static string ReadString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement field) || !TryReadString(field, out string? result) ||
            string.IsNullOrEmpty(result) || result.Length > 2048)
        {
            throw new ArgumentException($"Invalid frontend configuration field: {property}.", nameof(value));
        }

        return result;
    }

    private static bool TryReadString(JsonElement value, out string? result)
    {
        result = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return result is not null;
    }

    private static bool ReadBoolean(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement field) ||
            (field.ValueKind is not JsonValueKind.True and not JsonValueKind.False))
        {
            throw new ArgumentException($"Invalid capability: {property}.", nameof(value));
        }

        return field.GetBoolean();
    }

    private static Uri RemoveTrailingSlash(Uri value) =>
        new(value.AbsoluteUri.TrimEnd('/'), UriKind.Absolute);
}
