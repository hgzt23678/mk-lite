namespace ActivityPub.Identity;

public sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public required Uri Authority { get; init; }
    public required string Audience { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;

    public void Validate(bool isProduction)
    {
        if (!Authority.IsAbsoluteUri || (isProduction && Authority.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("OIDC authority or audience is invalid.");
        }

        if (isProduction && !RequireHttpsMetadata)
        {
            throw new InvalidOperationException("OIDC HTTPS metadata cannot be disabled in Production.");
        }
    }
}
