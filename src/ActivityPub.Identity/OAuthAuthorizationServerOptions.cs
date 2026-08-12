namespace ActivityPub.Identity;

public sealed class OAuthAuthorizationServerOptions
{
    public const string SectionName = "OAuth";

    public bool Enabled { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan RefreshTokenReuseLeeway { get; init; } = TimeSpan.Zero;
    public string? InteractiveClientId { get; init; }
    public string CallbackPath { get; init; } = "/auth/callback";
    public string? SigningCertificatePath { get; init; }
    public string? SigningCertificatePasswordFile { get; init; }
    public string? EncryptionCertificatePath { get; init; }
    public string? EncryptionCertificatePasswordFile { get; init; }

    public void Validate(bool isProduction)
    {
        if (!Enabled)
        {
            return;
        }

        if (AccessTokenLifetime < TimeSpan.FromMinutes(5) || AccessTokenLifetime > TimeSpan.FromHours(24) ||
            RefreshTokenLifetime < TimeSpan.FromHours(1) || RefreshTokenLifetime > TimeSpan.FromDays(365) ||
            RefreshTokenLifetime <= AccessTokenLifetime ||
            RefreshTokenReuseLeeway < TimeSpan.Zero || RefreshTokenReuseLeeway > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("OAuth token lifetimes are outside the supported range.");
        }

        if (CallbackPath.Length == 0 || CallbackPath[0] != '/' ||
            CallbackPath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OAuth callback path is invalid.");
        }

        if (isProduction)
        {
            RequireAbsoluteFile(SigningCertificatePath, "OAuth:SigningCertificatePath");
            RequireAbsoluteFile(SigningCertificatePasswordFile, "OAuth:SigningCertificatePasswordFile");
            RequireAbsoluteFile(EncryptionCertificatePath, "OAuth:EncryptionCertificatePath");
            RequireAbsoluteFile(EncryptionCertificatePasswordFile, "OAuth:EncryptionCertificatePasswordFile");
            if (string.Equals(SigningCertificatePath, EncryptionCertificatePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth signing and encryption certificates must be separate in Production.");
            }
        }
    }

    private static void RequireAbsoluteFile(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"{name} must be an absolute secret-file path in Production.");
        }
    }
}
