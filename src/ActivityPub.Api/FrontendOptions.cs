namespace ActivityPub.Server;

internal sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public bool Enabled { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public string[] Scopes { get; init; } = ["openid", "profile", "offline_access", "activitypub.read", "activitypub.write"];

    public required Uri PublicBaseUri { get; init; }

    public required Uri Authority { get; init; }

    public Uri? SourceUrl { get; init; }

    public void Validate(bool isProduction)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Any(char.IsControl))
        {
            throw new InvalidOperationException("Frontend:ClientId is required when the frontend is enabled.");
        }

        if (Scopes.Length == 0 || Scopes.Any(scope => string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsControl)))
        {
            throw new InvalidOperationException("Frontend:Scopes must contain only non-empty OAuth scope names.");
        }

        if (!PublicBaseUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("Frontend:PublicBaseUri must be a canonical origin; Production requires HTTPS.");
        }

        string publicOrigin = PublicBaseUri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(PublicBaseUri.AbsoluteUri.TrimEnd('/'), publicOrigin, StringComparison.Ordinal) ||
            isProduction && PublicBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Frontend:PublicBaseUri must be a canonical origin; Production requires HTTPS.");
        }

        if (!Authority.IsAbsoluteUri || isProduction && Authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Frontend:Authority must be absolute; Production requires HTTPS.");
        }

        if (SourceUrl is null || !SourceUrl.IsAbsoluteUri || isProduction && SourceUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Frontend:SourceUrl must identify the corresponding frontend source; Production requires HTTPS.");
        }

        if (isProduction && (SourceUrl.IdnHost.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.IdnHost.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.IdnHost.Equals("example.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Frontend:SourceUrl contains a placeholder hostname.");
        }

    }
}
