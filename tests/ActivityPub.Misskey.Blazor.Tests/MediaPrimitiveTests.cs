using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MediaPrimitiveTests : BunitContext
{
    [Fact]
    public void BlurhashImagePreservesPinnedDomDrawContractLoadSwitchAndSameOriginBoundary()
    {
        var browser = new RecordingBlurhashInterop();
        Services.AddSingleton<IBlurhashImageInterop>(browser);

        IRenderedComponent<MkImgWithBlurhash> component = Render<MkImgWithBlurhash>(parameters => parameters
            .Add(value => value.Src, "/media/preview/image")
            .Add(value => value.Hash, "LEHV6nWB2yk8pyo0adR*.7kCMdnj")
            .Add(value => value.Alt, "alternate")
            .Add(value => value.Title, "title")
            .Add(value => value.Size, 32)
            .Add(value => value.Cover, false)
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "blurhash"));

        IElement root = component.Find("div.xubzgfgb.fixture");
        Assert.DoesNotContain("cover", root.ClassList);
        Assert.Equal("title", root.GetAttribute("title"));
        Assert.Equal("blurhash", root.GetAttribute("data-contract"));
        IElement canvas = component.Find("canvas");
        Assert.Equal("32", canvas.GetAttribute("width"));
        Assert.Equal("32", canvas.GetAttribute("height"));
        IElement image = component.Find("img");
        Assert.Equal("/media/preview/image", image.GetAttribute("src"));
        Assert.Equal("alternate", image.GetAttribute("alt"));
        Assert.Equal("LEHV6nWB2yk8pyo0adR*.7kCMdnj", Assert.Single(browser.Draws).Hash);

        image.TriggerEvent("onload", EventArgs.Empty);
        Assert.Empty(component.FindAll("canvas"));

        IRenderedComponent<MkImgWithBlurhash> remote = Render<MkImgWithBlurhash>(parameters => parameters
            .Add(value => value.Src, "https://tracker.invalid/image.png")
            .Add(value => value.Hash, "LEHV6nWB2yk8pyo0adR*.7kCMdnj"));
        Assert.Empty(remote.FindAll("img"));
        Assert.Single(remote.FindAll("canvas"));
    }

    [Theory]
    [InlineData("force", false, true)]
    [InlineData("respect", true, true)]
    [InlineData("respect", false, false)]
    [InlineData("ignore", true, false)]
    public void VideoAppliesPinnedNsfwPolicyAndRevealRehideBranches(
        string policy,
        bool sensitive,
        bool initiallyHidden)
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(policy));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        NoteMediaViewModel media = Media("video", "video/mp4", sensitive);

        IRenderedComponent<MkMediaVideo> component = Render<MkMediaVideo>(parameters => parameters
            .Add(value => value.Video, media)
            .AddUnmatched("class", "fixture"));

        component.WaitForAssertion(() =>
        {
            string expected = initiallyHidden
                ? "icozogqfvdetwohsdglrbswgrejoxbdj fixture"
                : "kkjnbbplepmiyuadieoenjgutgcmtsvu fixture";
            Assert.Equal(expected, component.Find("div").ClassName);
        });

        if (!initiallyHidden)
        {
            AssertVideoDom(component, media);
            component.Find("div > i.fa-eye-slash").Click();
            Assert.NotNull(component.Find("div.icozogqfvdetwohsdglrbswgrejoxbdj"));
        }
        else
        {
            component.Find("div.icozogqfvdetwohsdglrbswgrejoxbdj").Click();
            AssertVideoDom(component, media);
        }
    }

    [Fact]
    public async Task MediaBannerPreservesSensitiveAudioAndDownloadBranchesAndColdVolumeStorage()
    {
        var storage = new RecordingStorage();
        storage.Values["miux:mediaVolume"] = 0.25;
        var browser = new RecordingMediaInterop();
        Services.AddSingleton<IClientStorage>(storage);
        Services.AddSingleton<IMediaElementInterop>(browser);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());

        IRenderedComponent<MkMediaBanner> component = Render<MkMediaBanner>(parameters => parameters
            .Add(value => value.Media, Media("audio", "audio/ogg", sensitive: true))
            .AddUnmatched("class", "fixture"));
        IElement hidden = component.Find("div.mk-media-banner.fixture > div.sensitive");
        Assert.Contains("閲覧注意", hidden.TextContent, StringComparison.Ordinal);
        hidden.Click();

        component.WaitForAssertion(() =>
        {
            IElement audio = component.Find("div.mk-media-banner > div.audio > audio.audio");
            Assert.Equal("/media/audio", audio.GetAttribute("src"));
            Assert.Equal("audio description", audio.GetAttribute("title"));
            Assert.Equal("metadata", audio.GetAttribute("preload"));
            Assert.Equal(0.25, Assert.Single(browser.Attachments).InitialVolume);
        });

        await component.Instance.StoreVolumeAsync(0.7);
        Assert.Equal(0.7, Assert.IsType<double>(storage.Values["miux:mediaVolume"]));

        IRenderedComponent<MkMediaBanner> midi = Render<MkMediaBanner>(parameters => parameters
            .Add(value => value.Media, Media("midi", "audio/midi", sensitive: false)));
        IElement download = midi.Find("a.download");
        Assert.Equal("/media/midi", download.GetAttribute("href"));
        Assert.Equal("midi description", download.GetAttribute("download"));
    }

    [Fact]
    public void MediaImagePreservesPinnedSensitiveVisibleDomAndTypedViewerBoundary()
    {
        var browser = new RecordingBlurhashInterop();
        Services.AddSingleton<IBlurhashImageInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState("force"));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        NoteMediaViewModel media = Media("gif", "image/gif", sensitive: false);
        NoteMediaViewModel? opened = null;

        IRenderedComponent<MkMediaImage> component = Render<MkMediaImage>(parameters => parameters
            .Add(value => value.Image, media)
            .Add(value => value.OpenRequested, EventCallback.Factory.Create<NoteMediaViewModel>(this, value => opened = value))
            .AddUnmatched("class", "image")
            .AddUnmatched("data-id", media.Id));

        component.WaitForAssertion(() =>
        {
            IElement hidden = component.Find("div.qjewsnkg.image[data-id='gif']");
            Assert.Contains("閲覧注意", hidden.TextContent, StringComparison.Ordinal);
            Assert.Equal("gif description", component.Find(".xubzgfgb.bg").GetAttribute("title"));
        });

        component.Find("div.qjewsnkg").Click();
        IElement visible = component.Find("div.gqnyydlz.image[data-id='gif']");
        IElement anchor = visible.QuerySelector(":scope > a")!;
        Assert.Equal(media.Url, anchor.GetAttribute("href"));
        Assert.Equal("gif description", anchor.GetAttribute("title"));
        Assert.False(anchor.HasAttribute("target"));
        Assert.False(anchor.HasAttribute("rel"));
        IElement image = anchor.QuerySelector(".xubzgfgb > img")!;
        Assert.Equal(media.PreviewUrl, image.GetAttribute("src"));
        Assert.Equal("gif description", image.GetAttribute("alt"));
        Assert.DoesNotContain("cover", image.ParentElement!.ClassList);
        Assert.Equal("GIF", anchor.QuerySelector(":scope > .gif")!.TextContent);

        anchor.Click();
        Assert.Same(media, opened);
        visible.QuerySelector(":scope > button.hide")!.Click();
        Assert.NotNull(component.Find("div.qjewsnkg.image[data-id='gif']"));
        Assert.NotEmpty(browser.Draws);
    }

    [Theory]
    [InlineData(false, false, false, "/media/image/preview")]
    [InlineData(true, false, false, "/media/image")]
    [InlineData(false, true, false, "/media/image")]
    [InlineData(false, false, true, "/media/image/preview?static=1")]
    public void MediaImageSelectsRawOrStaticSourceFromPinnedSettings(
        bool raw,
        bool loadRawImages,
        bool disableShowingAnimatedImages,
        string expected)
    {
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(
            "ignore",
            loadRawImages,
            disableShowingAnimatedImages));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());

        IRenderedComponent<MkMediaImage> component = Render<MkMediaImage>(parameters => parameters
            .Add(value => value.Image, Media("image", "image/png", sensitive: true))
            .Add(value => value.Raw, raw));

        component.WaitForAssertion(() =>
            Assert.Equal(expected, component.Find(".gqnyydlz .xubzgfgb > img").GetAttribute("src")));
    }

    [Fact]
    public async Task MediaImageRejectsRemoteUrlsAndCancelsPendingSettingsOnDispose()
    {
        var settings = new CancellationRecordingDeviceState();
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        Services.AddSingleton<IPizzaxDeviceState>(settings);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());

        NoteMediaViewModel remote = Media("remote", "image/png", sensitive: false) with
        {
            Url = "https://tracker.invalid/image.png",
            PreviewUrl = "https://tracker.invalid/preview.png"
        };
        using IRenderedComponent<MkMediaImage> rejected = Render<MkMediaImage>(parameters => parameters
            .Add(value => value.Image, remote));
        Assert.Empty(rejected.FindAll("a, img, canvas"));

        IRenderedComponent<MkMediaImage> pending = Render<MkMediaImage>(parameters => parameters
            .Add(value => value.Image, Media("pending", "image/png", sensitive: false)));
        await settings.WaitUntilReadStartedAsync();
        pending.Instance.Dispose();
        Assert.True(settings.Token.IsCancellationRequested);
    }

    [Fact]
    public void MediaListUsesPortedPrimitivesAndSupportsForceRevealAndRehide()
    {
        var blurhash = new RecordingBlurhashInterop();
        var gallery = new RecordingMediaGalleryInterop();
        Services.AddSingleton<IBlurhashImageInterop>(blurhash);
        Services.AddSingleton<IMediaGalleryInterop>(gallery);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState("force"));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IClientStorage>(new RecordingStorage());
        Services.AddSingleton<IMediaElementInterop>(new RecordingMediaInterop());
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        NoteMediaViewModel[] media =
        [
            Media("image", "image/png", sensitive: false),
            Media("video", "video/mp4", sensitive: false),
            Media("document", "application/pdf", sensitive: false),
            Media("unsafe-image", "image/heic", sensitive: false)
        ];

        IRenderedComponent<MkMediaList> component = Render<MkMediaList>(parameters => parameters
            .Add(value => value.Media, media)
            .AddUnmatched("class", "fixture-list")
            .AddUnmatched("data-contract", "media-list"));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".hoawjimk.fixture-list[data-contract='media-list']"));
            Assert.NotNull(component.Find(".qjewsnkg.image[data-id='image']"));
            Assert.NotNull(component.Find(".icozogqfvdetwohsdglrbswgrejoxbdj"));
            Assert.Equal(2, component.FindAll(".hoawjimk > .mk-media-banner > a.download").Count);
            Assert.Equal("2", component.Find(".gird-container > div").GetAttribute("data-count"));
            MediaGalleryAttachment attached = Assert.Single(gallery.Attachments);
            MediaGalleryItem item = Assert.Single(attached.Images);
            Assert.Equal("image", item.Id);
            Assert.Equal("/media/image", item.Src);
            Assert.Equal("/media/image/preview", item.Msrc);
        });

        component.Find(".qjewsnkg.image[data-id='image']").Click();
        Assert.NotNull(component.Find(".gqnyydlz.image[data-id='image'] .xubzgfgb"));
        Assert.Empty(overlays.Entries);
        component.Find(".gqnyydlz.image[data-id='image'] > button.hide").Click();
        Assert.NotNull(component.Find(".qjewsnkg.image[data-id='image']"));
        Assert.NotEmpty(blurhash.Draws);
    }

    [Fact]
    public void MediaListUsesTheExplicitImageCallbackWithoutAttachingPhotoSwipe()
    {
        var gallery = new RecordingMediaGalleryInterop();
        Services.AddSingleton<IMediaGalleryInterop>(gallery);
        Services.AddSingleton<IBlurhashImageInterop>(new RecordingBlurhashInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState("ignore"));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        NoteMediaViewModel? opened = null;
        NoteMediaViewModel image = Media("image", "image/png", sensitive: false);

        using IRenderedComponent<MkMediaList> component = Render<MkMediaList>(parameters => parameters
            .Add(value => value.Media, [image])
            .Add(value => value.ImageOpenRequested, selected => opened = selected));

        component.WaitForAssertion(() => Assert.NotNull(component.Find(".gqnyydlz.image > a")));
        component.Find(".gqnyydlz.image > a").Click();

        Assert.Same(image, opened);
        Assert.Empty(gallery.Attachments);
    }

    private static void AssertVideoDom(IRenderedComponent<MkMediaVideo> component, NoteMediaViewModel media)
    {
        IElement video = component.Find("div.kkjnbbplepmiyuadieoenjgutgcmtsvu > video");
        Assert.Equal(media.PreviewUrl, video.GetAttribute("poster"));
        Assert.Equal("none", video.GetAttribute("preload"));
        Assert.True(video.HasAttribute("controls"));
        IElement source = component.Find("video > source");
        Assert.Equal(media.Url, source.GetAttribute("src"));
        Assert.Equal(media.MediaType, source.GetAttribute("type"));
    }

    private static NoteMediaViewModel Media(string id, string mediaType, bool sensitive) => new(
        id,
        mediaType,
        $"/media/{id}",
        $"/media/{id}/preview",
        $"{id} description",
        "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
        640,
        360,
        sensitive);

    private sealed record BlurhashDraw(string? Hash, int Size);

    private sealed class RecordingBlurhashInterop : IBlurhashImageInterop
    {
        public List<BlurhashDraw> Draws { get; } = [];

        public ValueTask<bool> DrawAsync(
            ElementReference canvas,
            ElementReference image,
            string? hash,
            int size,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = canvas;
            _ = image;
            Draws.Add(new(hash, size));
            return ValueTask.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState(
        string policy,
        bool loadRawImages = false,
        bool disableShowingAnimatedImages = false) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName switch
            {
                "nsfw" => policy,
                "loadRawImages" => loadRawImages,
                "disableShowingAnimatedImages" => disableShowingAnimatedImages,
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CancellationRecordingDeviceState : IPizzaxDeviceState
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public async ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            _ = propertyName;
            Token = cancellationToken;
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return fallback;
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public Task WaitUntilReadStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingStorage : IClientStorage
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

        public ValueTask<T?> ReadAsync<T>(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = area;
            return ValueTask.FromResult(Values.TryGetValue(key, out object? value) ? (T?)value : default);
        }

        public ValueTask WriteAsync<T>(
            ClientStorageArea area,
            string key,
            T value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = area;
            Values[key] = value!;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = area;
            Values.Remove(key);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record MediaAttachment(double InitialVolume, DotNetObjectReference<MkMediaBanner> Receiver);

    private sealed class RecordingMediaInterop : IMediaElementInterop
    {
        public List<MediaAttachment> Attachments { get; } = [];

        public ValueTask<IJSObjectReference> AttachVolumeAsync(
            ElementReference element,
            double initialVolume,
            DotNetObjectReference<MkMediaBanner> receiver,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = element;
            Attachments.Add(new(initialVolume, receiver));
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record MediaGalleryAttachment(
        IReadOnlyList<MediaGalleryItem> Images,
        RecordingHandle Handle);

    private sealed class RecordingMediaGalleryInterop : IMediaGalleryInterop
    {
        public List<MediaGalleryAttachment> Attachments { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference gallery,
            IReadOnlyList<MediaGalleryItem> images,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = gallery;
            var handle = new RecordingHandle();
            Attachments.Add(new(images, handle));
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "sensitive" => "閲覧注意",
            "clickToShow" => "クリックして表示",
            "hide" => "隠す",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
