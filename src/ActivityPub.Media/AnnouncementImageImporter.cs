using System.Net;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;

namespace ActivityPub.Media;

internal sealed class AnnouncementImageImporter(
    ISafeFederationHttpClient httpClient,
    IDomainPolicyService policyService,
    IMediaService mediaService,
    MediaOptions options) : IAnnouncementImageImporter
{
    private static readonly HashSet<string> AcceptedMediaTypes = new(
        ["image/jpeg", "image/png", "image/gif", "image/webp"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<string?> ImportAsync(
        string? sourceImageUrl,
        string ownerActorIri,
        CancellationToken cancellationToken)
    {
        string? normalized = AnnouncementImageSource.Normalize(sourceImageUrl);
        if (normalized is null || normalized.StartsWith('/'))
        {
            return normalized;
        }

        Uri source = new(normalized, UriKind.Absolute);
        FederationPolicyKind policy = await policyService.GetEffectivePolicyAsync(
            source.IdnHost,
            actorIri: null,
            cancellationToken).ConfigureAwait(false);
        if (policy is FederationPolicyKind.Reject or FederationPolicyKind.RejectMedia)
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.RejectedByPolicy,
                "The remote announcement image is rejected by federation policy.");
        }

        SafeFederationResponse response;
        try
        {
            response = await httpClient.SendAsync(
                new SafeFederationRequest(
                    HttpMethod.Get,
                    source,
                    null,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    AcceptedMediaTypes,
                    Math.Min(
                        options.MaximumRemoteMediaBytes,
                        (int)Math.Min(options.MaximumUploadBytes, int.MaxValue))),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.RemoteFetchFailed,
                "The remote announcement image fetch timed out.");
        }
        catch (UnsafeFederationTargetException)
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.InvalidSource,
                "The remote announcement image target is unsafe.");
        }
        catch (HttpRequestException)
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.RemoteFetchFailed,
                "The remote announcement image could not be fetched.");
        }

        if (response.StatusCode != HttpStatusCode.OK || response.MediaType is null ||
            !AcceptedMediaTypes.Contains(response.MediaType))
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.RemoteFetchFailed,
                "The remote announcement image response was not an accepted image.");
        }

        string fileName = Path.GetFileName(response.FinalUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "announcement-image";
        }

        try
        {
            await using var content = new MemoryStream(response.Body, writable: false);
            MediaUploadResult uploaded = await mediaService.UploadAsync(
                new MediaUploadCommand(
                    ownerActorIri,
                    fileName,
                    response.MediaType,
                    Visibility.Public,
                    content),
                cancellationToken).ConfigureAwait(false);
            return $"/media/{uploaded.Id}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or DomainException)
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.ProcessingFailed,
                "The remote announcement image failed media safety processing.");
        }
    }
}

internal sealed class DisabledAnnouncementImageImporter : IAnnouncementImageImporter
{
    public Task<string?> ImportAsync(
        string? sourceImageUrl,
        string ownerActorIri,
        CancellationToken cancellationToken)
    {
        _ = ownerActorIri;
        string? normalized = AnnouncementImageSource.Normalize(sourceImageUrl);
        if (normalized is not null && !normalized.StartsWith('/'))
        {
            throw new AnnouncementImageImportException(
                AnnouncementImageImportFailure.MediaUnavailable,
                "Remote announcement image import is unavailable.");
        }

        return Task.FromResult(normalized);
    }
}

internal static class AnnouncementImageSource
{
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length is 0 or > 2_048 || normalized.Any(char.IsControl) || normalized.Contains('\\'))
        {
            throw InvalidSource();
        }

        if (normalized.StartsWith('/') && !normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw InvalidSource();
        }

        return uri.AbsoluteUri;
    }

    private static AnnouncementImageImportException InvalidSource() => new(
        AnnouncementImageImportFailure.InvalidSource,
        "The announcement image source is invalid.");
}
