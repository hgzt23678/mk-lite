using System.Security.Cryptography;
using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Media;

internal sealed class MediaService(
    IMediaRepository repository,
    IMediaObjectStore objectStore,
    IMediaMalwareScanner malwareScanner,
    IMediaProcessor processor,
    MediaOptions options,
    IClock clock) : IMediaService
{
    public async Task<MediaUploadResult> UploadAsync(MediaUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string owner = CanonicalIri.RequireAbsoluteHttp(command.OwnerActorIri, nameof(command.OwnerActorIri));
        string fileName = SanitizeFileName(command.OriginalFileName);
        string workingDirectory = Path.Combine(Path.GetTempPath(), "activitypub-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        string sourcePath = Path.Combine(workingDirectory, "source.upload");
        MediaResource? media = null;
        try
        {
            (long sourceLength, string sourceHash, string detectedType) = await SpoolAsync(
                command.Content,
                sourcePath,
                options.MaximumUploadBytes,
                cancellationToken).ConfigureAwait(false);
            ValidateDeclaredType(command.DeclaredMediaType, detectedType);
            DateTimeOffset now = clock.UtcNow;
            Guid mediaId = Guid.NewGuid();
            string quarantineKey = $"quarantine/{mediaId:N}/source{MediaTypeSniffer.Extension(detectedType)}";
            media = MediaResource.Create(owner, quarantineKey, sourceHash, detectedType, fileName, sourceLength, command.Visibility, now);
            await repository.AddAsync(media, cancellationToken).ConfigureAwait(false);

            await using (FileStream source = OpenRead(sourcePath))
            {
                await objectStore.PutAsync(quarantineKey, source, detectedType, cancellationToken).ConfigureAwait(false);
            }

            MalwareScanResult sourceScan = await ScanFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!sourceScan.IsClean)
            {
                media.Reject("Malware scanner rejected the upload.", clock.UtcNow);
                await repository.UpdateAsync(media, cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("The uploaded file failed the malware scan.");
            }

            ProcessedMedia processed = await processor.ProcessAsync(sourcePath, detectedType, workingDirectory, cancellationToken).ConfigureAwait(false);
            MalwareScanResult processedScan = await ScanFileAsync(processed.Path, cancellationToken).ConfigureAwait(false);
            if (!processedScan.IsClean)
            {
                media.Reject("Malware scanner rejected processed media.", clock.UtcNow);
                await repository.UpdateAsync(media, cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("Processed media failed the malware scan.");
            }

            string finalKey = $"media/{media.Id:N}/original{MediaTypeSniffer.Extension(processed.MediaType)}";
            await using (FileStream output = OpenRead(processed.Path))
            {
                await objectStore.PutAsync(finalKey, output, processed.MediaType, cancellationToken).ConfigureAwait(false);
            }

            string? thumbnailKey = null;
            if (processed.ThumbnailPath is not null)
            {
                MalwareScanResult thumbnailScan = await ScanFileAsync(processed.ThumbnailPath, cancellationToken).ConfigureAwait(false);
                if (!thumbnailScan.IsClean)
                {
                    throw new InvalidDataException("Generated thumbnail failed the malware scan.");
                }

                thumbnailKey = $"media/{media.Id:N}/thumbnail.jpg";
                await using FileStream thumbnail = OpenRead(processed.ThumbnailPath);
                await objectStore.PutAsync(thumbnailKey, thumbnail, "image/jpeg", cancellationToken).ConfigureAwait(false);
            }

            string outputHash = await HashFileAsync(processed.Path, cancellationToken).ConfigureAwait(false);
            media.MarkAvailable(
                finalKey,
                outputHash,
                processed.MediaType,
                processed.Length,
                processed.Width,
                processed.Height,
                processed.DurationMilliseconds,
                thumbnailKey,
                clock.UtcNow);
            await repository.UpdateAsync(media, cancellationToken).ConfigureAwait(false);
            await objectStore.DeleteAsync(quarantineKey, cancellationToken).ConfigureAwait(false);
            return new(media.Id, media.DetectedMediaType, media.Length, media.Width, media.Height, media.DurationMilliseconds);
        }
        catch (Exception)
        {
            if (media is not null && media.State == MediaState.PendingScan)
            {
                media.Quarantine("Media processing failed; inspect the protected quarantine object and service logs.", clock.UtcNow);
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await repository.UpdateAsync(media, cleanupTimeout.Token).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            TryDeleteWorkingDirectory(workingDirectory);
        }
    }

    public async Task<MediaDownload?> OpenReadAsync(Guid id, string? requesterActorIri, CancellationToken cancellationToken)
    {
        MediaResource? media = await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (media is null || media.State != MediaState.Available)
        {
            return null;
        }

        bool isPublic = media.Visibility is Visibility.Public or Visibility.Unlisted;
        if (!isPublic && (requesterActorIri is null ||
            !await repository.IsAuthorizedAsync(id, requesterActorIri, cancellationToken).ConfigureAwait(false)))
        {
            return null;
        }

        Stream stream = await objectStore.OpenReadAsync(media.StorageKey, cancellationToken).ConfigureAwait(false);
        return new(
            stream,
            media.DetectedMediaType,
            media.Length,
            media.OriginalFileName,
            isPublic,
            $"\"{media.ContentHash}\"",
            media.UpdatedAt);
    }

    private async Task<MalwareScanResult> ScanFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(path);
        return await malwareScanner.ScanAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(long Length, string Hash, string MediaType)> SpoolAsync(
        Stream input,
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        byte[] header = new byte[16];
        int headerLength = 0;
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Media exceeds the configured upload limit.");
            }

            int headerCopy = Math.Min(read, header.Length - headerLength);
            if (headerCopy > 0)
            {
                buffer.AsSpan(0, headerCopy).CopyTo(header.AsSpan(headerLength));
                headerLength += headerCopy;
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (total == 0)
        {
            throw new InvalidDataException("Media upload is empty.");
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return (total, Convert.ToHexStringLower(hash.GetHashAndReset()), MediaTypeSniffer.Detect(header.AsSpan(0, headerLength)));
    }

    private static void ValidateDeclaredType(string? declaredType, string detectedType)
    {
        if (string.IsNullOrWhiteSpace(declaredType) || string.Equals(declaredType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(declaredType.Split(';', 2)[0].Trim(), detectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Declared and detected media types do not match.");
        }
    }

    private static string SanitizeFileName(string value)
    {
        string fileName = Path.GetFileName(value ?? string.Empty);
        Span<char> characters = fileName.Length <= 128 ? stackalloc char[fileName.Length] : stackalloc char[128];
        int written = 0;
        foreach (char character in fileName)
        {
            if (written == characters.Length)
            {
                break;
            }

            characters[written++] = char.IsControl(character) || character is '/' or '\\' or '"' or '\'' or ';'
                ? '_'
                : character;
        }

        string result = new(characters[..written]);
        return string.IsNullOrWhiteSpace(result) ? "media" : result;
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream content = OpenRead(path);
        byte[] digest = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }

    private static void TryDeleteWorkingDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
