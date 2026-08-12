namespace ActivityPub.Identity;

public sealed class VaultTransitOptions
{
    public const string SectionName = "VaultTransit";

    public required Uri Address { get; init; }
    public string Mount { get; init; } = "transit";
    public required string TokenFile { get; init; }
    public string? Namespace { get; init; }

    public void Validate(bool isProduction)
    {
        if (!Address.IsAbsoluteUri || (isProduction && Address.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("VaultTransit:Address must be absolute and use HTTPS in Production.");
        }

        if (string.IsNullOrWhiteSpace(Mount) || Mount.Contains('/', StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(TokenFile) || !Path.IsPathFullyQualified(TokenFile))
        {
            throw new InvalidOperationException("Vault Transit mount or token-file configuration is invalid.");
        }
    }
}
