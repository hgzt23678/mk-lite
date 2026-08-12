using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ImageViewerTests : BunitContext
{
    [Fact]
    public async Task PreservesPinnedDomMetadataCloseAnimationBoundaryAndCleanup()
    {
        var dialog = new RecordingDialogInterop();
        var viewer = new RecordingImageViewerInterop();
        Services.AddSingleton<IDialogWindowInterop>(dialog);
        Services.AddSingleton<IImageViewerInterop>(viewer);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        int closed = 0;
        NoteMediaViewModel image = Image() with { Size = 1536 };

        IRenderedComponent<MkImageViewer> component = Render<MkImageViewer>(parameters => parameters
            .Add(value => value.Image, image)
            .Add(value => value.Closed, EventCallback.Factory.Create(this, () => closed++)));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, dialog.MiddleAttachments);
            Assert.Equal(1, viewer.Attachments);
        });
        IElement modal = component.Find(".qzhlnise.dialog.modal-enter-active.modal-enter-from");
        Assert.Equal("dialog", modal.GetAttribute("role"));
        Assert.Equal("true", modal.GetAttribute("aria-modal"));
        Assert.Equal("image description", modal.GetAttribute("aria-label"));
        Assert.NotNull(component.Find(".qzhlnise > .bg._modalBg"));
        IElement root = component.Find(".content > .xubzgfga");
        Assert.Equal("image description", root.QuerySelector(":scope > header")!.TextContent);
        IElement renderedImage = root.QuerySelector(":scope > img")!;
        Assert.Equal("/media/image", renderedImage.GetAttribute("src"));
        Assert.Equal("image description", renderedImage.GetAttribute("alt"));
        Assert.Equal("image description", renderedImage.GetAttribute("title"));
        string[] metadata = root.QuerySelectorAll(":scope > footer > span").Select(value => value.TextContent).ToArray();
        Assert.Equal(["image/png", "2KB", "1,920px × 1,080px"], metadata);

        renderedImage.Click();
        Assert.Contains("close", dialog.Handle.Invocations);
        await component.InvokeAsync(component.Instance.NotifyClosed);
        Assert.Equal(1, closed);

        await component.Instance.DisposeAsync();
        Assert.True(dialog.Handle.Disposed);
        Assert.True(viewer.Handle.Disposed);
        Assert.Contains("dispose", dialog.Handle.Invocations);
        Assert.Contains("dispose", viewer.Handle.Invocations);
    }

    [Fact]
    public void RejectsAnUnproxiedRemoteImageBeforeDomOrBrowserAttachment()
    {
        var dialog = new RecordingDialogInterop();
        var viewer = new RecordingImageViewerInterop();
        Services.AddSingleton<IDialogWindowInterop>(dialog);
        Services.AddSingleton<IImageViewerInterop>(viewer);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        NoteMediaViewModel remote = Image() with { Url = "https://tracker.invalid/image.png" };

        using IRenderedComponent<MkImageViewer> component = Render<MkImageViewer>(parameters => parameters
            .Add(value => value.Image, remote));

        Assert.Empty(component.FindAll("div, img, header, footer"));
        Assert.Equal(0, dialog.MiddleAttachments);
        Assert.Equal(0, viewer.Attachments);
        Assert.DoesNotContain("tracker.invalid", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayServiceCreatesARealViewerEntryAndRejectsRemoteUrls()
    {
        var overlays = new MisskeyOverlayService();
        NoteMediaViewModel image = Image();

        Guid id = overlays.ShowImageViewer(image);

        MisskeyOverlayEntry entry = Assert.Single(overlays.Entries);
        Assert.Equal(id, entry.Id);
        Assert.Equal(MisskeyOverlayKind.ImageViewer, entry.Kind);
        Assert.Same(image, entry.ImageViewer?.Image);
        Assert.Throws<ArgumentException>(() => overlays.ShowImageViewer(
            image with { Url = "https://tracker.invalid/image.png" }));
    }

    private static NoteMediaViewModel Image() => new(
        "image",
        "image/png",
        "/media/image",
        "/media/image/preview",
        "image description",
        "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
        1920,
        1080,
        Sensitive: false);

    private sealed class RecordingDialogInterop : IDialogWindowInterop
    {
        public RecordingHandle Handle { get; } = new();
        public int MiddleAttachments { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(Handle);

        public ValueTask<IJSObjectReference> AttachMiddlePriorityAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = modal;
            _ = content;
            _ = window;
            _ = receiver;
            MiddleAttachments++;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingImageViewerInterop : IImageViewerInterop
    {
        public RecordingHandle Handle { get; } = new();
        public int Attachments { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference modal,
            ElementReference viewport,
            ElementReference image,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = modal;
            _ = viewport;
            _ = image;
            Attachments++;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;

        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
