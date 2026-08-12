using System.Net;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Media;

namespace ActivityPub.Media.Tests;

public sealed class AnnouncementImageImporterTests
{
    private static readonly Guid ImportedMediaId = Guid.Parse("0199069e-f0fe-7fed-a64d-2b92941b8271");

    [Fact]
    public async Task RemoteImageUsesSafeFetchAndDurableMediaPipeline()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47];
        var http = new RecordingHttpClient(new(
            HttpStatusCode.OK,
            new Uri("https://cdn.remote.example/images/banner.png"),
            "image/png",
            bytes,
            "\"image-v1\"",
            null,
            null));
        var media = new RecordingMediaService(ImportedMediaId);
        var importer = new AnnouncementImageImporter(
            http,
            new FixedPolicy(FederationPolicyKind.Allow),
            media,
            new MediaOptions
            {
                MaximumRemoteMediaBytes = 2_048,
                MaximumUploadBytes = 1_024
            });

        string? result = await importer.ImportAsync(
            "https://cdn.remote.example/images/banner.png",
            "https://local.example/users/admin",
            CancellationToken.None);

        Assert.Equal($"/media/{ImportedMediaId}", result);
        SafeFederationRequest request = Assert.IsType<SafeFederationRequest>(http.Request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://cdn.remote.example/images/banner.png", request.Uri.AbsoluteUri);
        Assert.Equal(1_024, request.MaximumResponseBytes);
        Assert.Contains("image/png", request.AcceptedMediaTypes);
        Assert.Equal("https://local.example/users/admin", media.OwnerActorIri);
        Assert.Equal("banner.png", media.FileName);
        Assert.Equal("image/png", media.DeclaredMediaType);
        Assert.Equal(Visibility.Public, media.Visibility);
        Assert.Equal(bytes, media.Content);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" /media/existing-id ", "/media/existing-id")]
    public async Task SameOriginOrAbsentImageDoesNotPerformRemoteWork(string? source, string? expected)
    {
        var http = new RecordingHttpClient(null);
        var media = new RecordingMediaService(ImportedMediaId);
        var importer = new AnnouncementImageImporter(
            http,
            new FixedPolicy(FederationPolicyKind.Allow),
            media,
            new MediaOptions());

        string? result = await importer.ImportAsync(
            source,
            "https://local.example/users/admin",
            CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Null(http.Request);
        Assert.Null(media.OwnerActorIri);
    }

    [Theory]
    [InlineData(FederationPolicyKind.Reject)]
    [InlineData(FederationPolicyKind.RejectMedia)]
    public async Task RejectedDomainNeverFetchesOrUploads(FederationPolicyKind policy)
    {
        var http = new RecordingHttpClient(null);
        var media = new RecordingMediaService(ImportedMediaId);
        var importer = new AnnouncementImageImporter(http, new FixedPolicy(policy), media, new MediaOptions());

        AnnouncementImageImportException error = await Assert.ThrowsAsync<AnnouncementImageImportException>(() =>
            importer.ImportAsync(
                "https://blocked.example/banner.png",
                "https://local.example/users/admin",
                CancellationToken.None));

        Assert.Equal(AnnouncementImageImportFailure.RejectedByPolicy, error.Failure);
        Assert.Null(http.Request);
        Assert.Null(media.OwnerActorIri);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@remote.example/image.png")]
    [InlineData("//remote.example/image.png")]
    [InlineData("https://remote.example/image.png#fragment")]
    public async Task InvalidSourceIsRejectedBeforeNetworkAccess(string source)
    {
        var http = new RecordingHttpClient(null);
        var importer = new AnnouncementImageImporter(
            http,
            new FixedPolicy(FederationPolicyKind.Allow),
            new RecordingMediaService(ImportedMediaId),
            new MediaOptions());

        AnnouncementImageImportException error = await Assert.ThrowsAsync<AnnouncementImageImportException>(() =>
            importer.ImportAsync(source, "https://local.example/users/admin", CancellationToken.None));

        Assert.Equal(AnnouncementImageImportFailure.InvalidSource, error.Failure);
        Assert.Null(http.Request);
    }

    [Fact]
    public async Task NetworkTimeoutIsAControlledImportFailure()
    {
        var importer = new AnnouncementImageImporter(
            new ThrowingHttpClient(new TaskCanceledException("fixture timeout")),
            new FixedPolicy(FederationPolicyKind.Allow),
            new RecordingMediaService(ImportedMediaId),
            new MediaOptions());

        AnnouncementImageImportException error = await Assert.ThrowsAsync<AnnouncementImageImportException>(() =>
            importer.ImportAsync(
                "https://remote.example/image.png",
                "https://local.example/users/admin",
                CancellationToken.None));

        Assert.Equal(AnnouncementImageImportFailure.RemoteFetchFailed, error.Failure);
    }

    [Fact]
    public async Task DisabledMediaRejectsRemoteSourceButPreservesSameOriginPath()
    {
        var importer = new DisabledAnnouncementImageImporter();

        Assert.Equal(
            "/media/existing-id",
            await importer.ImportAsync(
                "/media/existing-id",
                "https://local.example/users/admin",
                CancellationToken.None));
        AnnouncementImageImportException error = await Assert.ThrowsAsync<AnnouncementImageImportException>(() =>
            importer.ImportAsync(
                "https://remote.example/image.png",
                "https://local.example/users/admin",
                CancellationToken.None));
        Assert.Equal(AnnouncementImageImportFailure.MediaUnavailable, error.Failure);
    }

    private sealed class RecordingHttpClient(SafeFederationResponse? response) : ISafeFederationHttpClient
    {
        public SafeFederationRequest? Request { get; private set; }

        public Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(response ?? throw new InvalidOperationException("HTTP was not expected."));
        }
    }

    private sealed class ThrowingHttpClient(Exception exception) : ISafeFederationHttpClient
    {
        public Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken) => Task.FromException<SafeFederationResponse>(exception);
    }

    private sealed class RecordingMediaService(Guid resultId) : IMediaService
    {
        public string? OwnerActorIri { get; private set; }
        public string? FileName { get; private set; }
        public string? DeclaredMediaType { get; private set; }
        public Visibility? Visibility { get; private set; }
        public byte[]? Content { get; private set; }

        public async Task<MediaUploadResult> UploadAsync(
            MediaUploadCommand command,
            CancellationToken cancellationToken)
        {
            OwnerActorIri = command.OwnerActorIri;
            FileName = command.OriginalFileName;
            DeclaredMediaType = command.DeclaredMediaType;
            Visibility = command.Visibility;
            using var copy = new MemoryStream();
            await command.Content.CopyToAsync(copy, cancellationToken);
            Content = copy.ToArray();
            return new(resultId, command.DeclaredMediaType ?? "application/octet-stream", Content.Length, null, null, null);
        }

        public Task<MediaDownload?> OpenReadAsync(
            Guid id,
            string? requesterActorIri,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedPolicy(FederationPolicyKind policy) : IDomainPolicyService
    {
        public Task<FederationPolicyKind> GetEffectivePolicyAsync(
            string domain,
            string? actorIri,
            CancellationToken cancellationToken) => Task.FromResult(policy);

        public Task<IReadOnlySet<string>> FindRejectedActorsAsync(
            IReadOnlyCollection<string> actorIris,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
