using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class DriveFileThumbnailTests : BunitContext
{
    [Fact]
    public void ImageWithThumbnailRendersBlurhashImageWithoutIcon()
    {
        var browser = new RecordingBlurhashInterop();
        Services.AddSingleton<IBlurhashImageInterop>(browser);
        var file = new ComposerMediaViewModel(
            Guid.NewGuid(),
            "photo.png",
            "image/png",
            "/media/photo.png",
            "/media/photo-thumb.png",
            Sensitive: false,
            Description: null,
            Width: 640,
            Height: 480);

        using IRenderedComponent<MkDriveFileThumbnail> component = Render<MkDriveFileThumbnail>(parameters => parameters
            .Add(thumbnail => thumbnail.File, file));

        Assert.NotNull(component.Find(".zdjebgpv img[src='/media/photo-thumb.png']"));
        Assert.Empty(component.FindAll(".zdjebgpv > i.icon"));
    }

    [Fact]
    public void VideoWithThumbnailRendersFilmSubIcon()
    {
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        var file = new ComposerMediaViewModel(
            Guid.NewGuid(),
            "clip.mp4",
            "video/mp4",
            "/media/clip.mp4",
            "/media/clip-thumb.png",
            Sensitive: false,
            Description: null,
            Width: 1280,
            Height: 720);

        using IRenderedComponent<MkDriveFileThumbnail> component = Render<MkDriveFileThumbnail>(parameters => parameters
            .Add(thumbnail => thumbnail.File, file));

        Assert.NotNull(component.Find(".zdjebgpv img[src='/media/clip-thumb.png']"));
        Assert.NotNull(component.Find(".zdjebgpv > i.fas.fa-film.icon-sub"));
    }

    [Fact]
    public void VideoWithoutThumbnailFallsBackToFileVideoIcon()
    {
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        var file = new ComposerMediaViewModel(
            Guid.NewGuid(),
            "clip.mp4",
            "video/mp4",
            "/media/clip.mp4",
            "/media/clip.mp4",
            Sensitive: false,
            Description: null,
            Width: 1280,
            Height: 720);

        using IRenderedComponent<MkDriveFileThumbnail> component = Render<MkDriveFileThumbnail>(parameters => parameters
            .Add(thumbnail => thumbnail.File, file));

        Assert.NotNull(component.Find(".zdjebgpv > i.fas.fa-file-video.icon"));
        Assert.Empty(component.FindAll(".zdjebgpv > img"));
    }

    [Theory]
    [InlineData("text/plain", "fa-file-alt")]
    [InlineData("application/zip", "fa-file-archive")]
    [InlineData("application/pdf", "fa-file-pdf")]
    [InlineData("text/csv", "fa-file-csv")]
    [InlineData("application/octet-stream", "fa-file")]
    public void MediaTypeMapsToThePinnedIcon(string mediaType, string expectedIcon)
    {
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        var file = new ComposerMediaViewModel(
            Guid.NewGuid(),
            "file.bin",
            mediaType,
            "/media/file.bin",
            "/media/file.bin",
            Sensitive: false,
            Description: null,
            Width: null,
            Height: null);

        using IRenderedComponent<MkDriveFileThumbnail> component = Render<MkDriveFileThumbnail>(parameters => parameters
            .Add(thumbnail => thumbnail.File, file));

        IElement icon = component.Find(".zdjebgpv > i.icon");
        Assert.Contains(expectedIcon, icon.ClassName, StringComparison.Ordinal);
    }

    private sealed class RecordingBlurhashInterop : IBlurhashImageInterop
    {
        public ValueTask<bool> DrawAsync(
            ElementReference canvas,
            ElementReference image,
            string? hash,
            int size,
            CancellationToken cancellationToken)
        {
            _ = canvas;
            _ = image;
            _ = hash;
            _ = size;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
