using System.Security.Cryptography.X509Certificates;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace ActivityPub.Server;

internal static class DataProtectionConfiguration
{
    public static IServiceCollection AddActivityPubDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        IConfigurationSection section = configuration.GetSection("DataProtection");
        string applicationName = section["ApplicationName"] ?? "activitypub-server";
        string? certificatePath = section["CertificatePath"];
        string? passwordFile = section["CertificatePasswordFile"];
        if (isProduction && (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(passwordFile)))
        {
            throw new InvalidOperationException("DataProtection certificate and password secret-file paths are required in Production.");
        }

        IDataProtectionBuilder builder = services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToDbContext<FederationDbContext>();
        if (!string.IsNullOrWhiteSpace(certificatePath) || !string.IsNullOrWhiteSpace(passwordFile))
        {
            if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(passwordFile) ||
                !Path.IsPathFullyQualified(certificatePath) || !Path.IsPathFullyQualified(passwordFile))
            {
                throw new InvalidOperationException("DataProtection secret-file paths must both be absolute.");
            }

            string password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
            if (password.Length is < 12 or > 1_024)
            {
                throw new InvalidOperationException("DataProtection certificate password secret has an invalid length.");
            }

            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("DataProtection certificate has no private key.");
            }

            builder.ProtectKeysWithCertificate(certificate);
        }

        return services;
    }
}
