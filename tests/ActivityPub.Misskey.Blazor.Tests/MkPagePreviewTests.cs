using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkPagePreviewTests : BunitContext
{
    public MkPagePreviewTests()
    {
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<IClipboardInterop>(new ClipboardStub());
        Services.AddSingleton<IMisskeyLocalizer>(new PreviewLocalizer());
    }

    [Fact]
    public void PreservesUpstreamPagePreviewStructureSummaryAndSafeThumbnailBoundary()
    {
        string summary = new('x', 90);
        NoteAuthorViewModel user = new("user-1", "alice", "alice", "Alice", "https://local.example/avatar.png", false);
        using IRenderedComponent<MkPagePreview> component = Render<MkPagePreview>(parameters => parameters
            .Add(item => item.Page, new MisskeyPagePreviewViewModel("hello", "Hello page", summary, user, "https://remote.example/page.png")));

        AngleSharp.Dom.IElement root = component.Find("a.vhpxefrj._block");
        Assert.Equal("/@alice/pages/hello", root.GetAttribute("href"));
        Assert.Contains("https://remote.example/page.png", root.QuerySelector(".thumbnail")?.GetAttribute("style"));
        Assert.Equal(86, root.QuerySelector("article > p")?.TextContent.Length);
        Assert.Equal("Alice", root.QuerySelector("footer > p")?.TextContent);
    }

    [Fact]
    public void RejectsUnsafeThumbnailInsteadOfRenderingJavascriptUrl()
    {
        NoteAuthorViewModel user = new("user-1", "alice", "alice", "Alice", "https://local.example/avatar.png", false);
        using IRenderedComponent<MkPagePreview> component = Render<MkPagePreview>(parameters => parameters
            .Add(item => item.Page, new MisskeyPagePreviewViewModel("hello", "Hello", "summary", user, "javascript:alert(1)")));

        Assert.Empty(component.FindAll(".thumbnail"));
    }

    private sealed class ClipboardStub : IClipboardInterop
    {
        public ValueTask<ClipboardWriteResult> WriteTextAsync(string text, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ClipboardWriteResult(true, "test", null));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PreviewLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => false;
    }
}
