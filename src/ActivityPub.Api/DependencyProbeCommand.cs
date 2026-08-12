using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Server;

internal static class DependencyProbeCommand
{
    private const string ProtectedPrefix = "activitypub-recovery-drill:";
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static async Task RunAsync(
        IServiceProvider services,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Length is < 1 or > 2)
        {
            throw new ArgumentException(
                "Usage: dependency-probe postgres|vault|media|media-create|media-open|media-cleanup|data-protection-protect|data-protection-unprotect [payload]");
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        object result = arguments[0] switch
        {
            "postgres" => await ProbePostgreSqlAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
            "vault" => await ProbeVaultAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
            "media" => await ProbeMediaAsync(scope.ServiceProvider, cleanup: true, cancellationToken).ConfigureAwait(false),
            "media-create" => await ProbeMediaAsync(scope.ServiceProvider, cleanup: false, cancellationToken).ConfigureAwait(false),
            "media-open" when arguments.Length == 2 => await ProbeExistingMediaAsync(scope.ServiceProvider, arguments[1], cancellationToken).ConfigureAwait(false),
            "media-cleanup" when arguments.Length == 2 => await CleanupMediaAsync(scope.ServiceProvider, arguments[1], cancellationToken).ConfigureAwait(false),
            "data-protection-protect" => ProbeDataProtectionProtect(scope.ServiceProvider),
            "data-protection-unprotect" when arguments.Length == 2 => ProbeDataProtectionUnprotect(scope.ServiceProvider, arguments[1]),
            _ => throw new ArgumentException("Unknown dependency probe or missing protected payload.")
        };
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    private static async Task<object> ProbePostgreSqlAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        IDbContextFactory<FederationDbContext> factory = services.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool connected = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        long deliveries = await db.Deliveries.LongCountAsync(cancellationToken).ConfigureAwait(false);
        return new { probe = "postgres", connected, deliveries };
    }

    private static async Task<object> ProbeVaultAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        const string handle = "activitypub-recovery-drill-probe";
        ExternalKeyProvision provision = await services.GetRequiredService<IExternalKeyProvisioner>()
            .CreateRsaKeyAsync(handle, cancellationToken).ConfigureAwait(false);
        byte[] message = SHA256.HashData("activitypub-vault-probe"u8);
        byte[] signature = await services.GetRequiredService<IKeySigner>()
            .SignAsync(provision.Handle, "rsa-v1_5-sha256", message, cancellationToken)
            .ConfigureAwait(false);
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(provision.PublicKeyPem);
        if (!rsa.VerifyData(message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new CryptographicException("Vault returned a signature that does not verify with its public key.");
        }

        return new { probe = "vault", handle, signatureVerified = true };
    }

    private static async Task<object> ProbeMediaAsync(
        IServiceProvider services,
        bool cleanup,
        CancellationToken cancellationToken)
    {
        await using var input = new MemoryStream(OnePixelPng, writable: false);
        IMediaService mediaService = services.GetRequiredService<IMediaService>();
        MediaUploadResult uploaded = await mediaService.UploadAsync(
            new MediaUploadCommand(
                "https://recovery-drill.invalid/users/probe",
                "probe.png",
                "image/png",
                Visibility.Public,
                input),
            cancellationToken).ConfigureAwait(false);
        MediaDownload download = await mediaService.OpenReadAsync(uploaded.Id, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Uploaded recovery probe media was not readable from object storage.");
        await using (download.Content)
        {
            byte[] received = await ReadBoundedAsync(download.Content, 2_000_000, cancellationToken).ConfigureAwait(false);
            if (received.Length == 0)
            {
                throw new InvalidDataException("Object storage returned an empty recovery probe object.");
            }
        }

        if (cleanup)
        {
            await MarkMediaDeletedAsync(services, uploaded.Id, cancellationToken).ConfigureAwait(false);
        }

        return new { probe = cleanup ? "media" : "media-create", uploaded.Id, uploaded.MediaType, uploaded.Length, roundTrip = true, cleanupScheduled = cleanup };
    }

    private static async Task<object> ProbeExistingMediaAsync(
        IServiceProvider services,
        string id,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out Guid mediaId))
        {
            throw new ArgumentException("Media recovery probe id is invalid.", nameof(id));
        }

        MediaDownload download = await services.GetRequiredService<IMediaService>()
            .OpenReadAsync(mediaId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Restored media metadata is absent, private, or not available.");
        await using (download.Content)
        {
            byte[] received = await ReadBoundedAsync(download.Content, 2_000_000, cancellationToken).ConfigureAwait(false);
            if (received.Length == 0)
            {
                throw new InvalidDataException("Restored object storage media is empty.");
            }

            return new
            {
                probe = "media-open",
                id = mediaId,
                restored = true,
                contentHash = PayloadDigest.Sha256Hex(received),
                length = received.Length
            };
        }
    }

    private static async Task<object> CleanupMediaAsync(
        IServiceProvider services,
        string id,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out Guid mediaId))
        {
            throw new ArgumentException("Media cleanup probe id is invalid.", nameof(id));
        }

        await MarkMediaDeletedAsync(services, mediaId, cancellationToken).ConfigureAwait(false);
        return new { probe = "media-cleanup", id = mediaId, cleanupScheduled = true };
    }

    private static async Task MarkMediaDeletedAsync(
        IServiceProvider services,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        IMediaRepository repository = services.GetRequiredService<IMediaRepository>();
        MediaResource persisted = await repository.FindAsync(mediaId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Recovery probe media metadata disappeared after upload.");
        persisted.Delete(DateTimeOffset.UtcNow);
        await repository.UpdateAsync(persisted, cancellationToken).ConfigureAwait(false);
    }

    private static object ProbeDataProtectionProtect(IServiceProvider services)
    {
        string marker = ProtectedPrefix + Guid.NewGuid().ToString("N");
        string protectedPayload = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("activitypub-production-recovery-drill-v1")
            .Protect(marker);
        return new { probe = "data-protection-protect", protectedPayload };
    }

    private static object ProbeDataProtectionUnprotect(IServiceProvider services, string protectedPayload)
    {
        if (protectedPayload.Length is < 16 or > 64_000 || protectedPayload.Any(char.IsControl))
        {
            throw new ArgumentException("Protected recovery payload is malformed.", nameof(protectedPayload));
        }

        string marker = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("activitypub-production-recovery-drill-v1")
            .Unprotect(protectedPayload);
        if (!marker.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The restored Data Protection key ring returned an unexpected marker.");
        }

        return new { probe = "data-protection-unprotect", restored = true, markerHash = PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(marker)) };
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        while (output.Length <= maximumBytes)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidDataException("Recovery probe object exceeded its bounded read limit.");
    }
}
