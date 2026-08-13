using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class RemoteMediaRenderingBoundaryTests : BunitContext
{
    [Fact]
    public void AvatarAndCustomEmojiRequireSameOriginProxyPaths()
    {
        var author = new NoteAuthorViewModel(
            "user", "alice", "alice@example.test", "Alice",
            "https://tracker.example/avatar.png", IsBot: false);
        IRenderedComponent<MkAvatar> avatar = Render<MkAvatar>(parameters => parameters
            .Add(component => component.User, author));
        IRenderedComponent<MkEmoji> emoji = Render<MkEmoji>(parameters => parameters
            .Add(component => component.Emoji, ":party:")
            .Add(component => component.CustomUrl, "https://tracker.example/emoji.png"));

        Assert.Equal("/static-assets/user-unknown.png", avatar.Find("img.inner").GetAttribute("src"));
        Assert.DoesNotContain("tracker.example", avatar.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("tracker.example", emoji.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaListDropsUnproxiedRemoteMediaButRendersSameOriginCacheUrls()
    {
        Services.AddSingleton<IBlurhashImageInterop>(new NoopBlurhashInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new DefaultDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(new BoundaryLocalizer());
        Services.AddSingleton<IMediaGalleryInterop>(new NoopMediaGalleryInterop());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        NoteMediaViewModel remote = Media(
            "remote",
            "https://tracker.example/image.png",
            "https://tracker.example/preview.png");
        NoteMediaViewModel cached = Media(
            "cached",
            "/media/proxy/00000000-0000-0000-0000-000000000001/source",
            "/media/proxy/00000000-0000-0000-0000-000000000001/preview");

        IRenderedComponent<MkMediaList> component = Render<MkMediaList>(parameters => parameters
            .Add(value => value.Media, [remote, cached]));

        Assert.DoesNotContain("tracker.example", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".gird-container img"));
        Assert.Equal(cached.PreviewUrl, component.Find(".gird-container img").GetAttribute("src"));
        Assert.Equal(cached.Url, component.Find(".gird-container a").GetAttribute("href"));
        Assert.Equal("1", component.Find(".gird-container > div").GetAttribute("data-count"));
    }

    [Fact]
    public void ReactionViewerDoesNotEmitAnUnproxiedCustomEmojiUrl()
    {
        Services.AddSingleton<IReactionDetailsPresentationService>(new EmptyReactionDetails());
        Services.AddSingleton<IReactionViewerInterop>(new NoopReactionViewerInterop());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        NoteViewModel note = Note() with
        {
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal) { [":party:"] = 1 },
            Emojis = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["party"] = "https://tracker.example/party.png"
            }
        };
        IRenderedComponent<MkReactionsViewer> component = Render<MkReactionsViewer>(parameters => parameters
            .Add(value => value.Note, note));

        Assert.Empty(component.FindAll("img"));
        Assert.Equal(":party:", component.Find("span.icon").TextContent);
        Assert.DoesNotContain("tracker.example", component.Markup, StringComparison.Ordinal);
    }

    private static NoteMediaViewModel Media(string id, string url, string previewUrl) =>
        new(id, "image/png", url, previewUrl, "fixture", null, 100, 100, Sensitive: false);

    private static NoteViewModel Note() => new(
        Guid.NewGuid(),
        "note",
        DateTimeOffset.UtcNow,
        new("user", "alice", "alice@example.test", "Alice", "/media/avatar", IsBot: false),
        "fixture",
        ContentWarning: null,
        Visibility.Public,
        ReplyId: null,
        RepliesCount: 0,
        RenotesCount: 0,
        ReactionsCount: 0,
        ReactedByViewer: false,
        Reactions: new Dictionary<string, long>(StringComparer.Ordinal),
        ViewerReaction: null,
        Media: [],
        Mentions: [],
        Hashtags: [],
        Emojis: new Dictionary<string, string>(StringComparer.Ordinal),
        Poll: null,
        Renote: null);

    private sealed class NoopBlurhashInterop : IBlurhashImageInterop
    {
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
            _ = hash;
            _ = size;
            return ValueTask.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyReactionDetails : IReactionDetailsPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            Guid postId,
            string reaction,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = postId;
            _ = reaction;
            _ = limit;
            return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>([]);
        }
    }

    private sealed class NoopReactionViewerInterop : IReactionViewerInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference target,
            DotNetObjectReference<MkReactionsViewerReaction> receiver,
            bool canToggle,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = target;
            _ = receiver;
            _ = canToggle;
            return ValueTask.FromResult<IJSObjectReference>(new NoopJsHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopMediaGalleryInterop : IMediaGalleryInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference gallery,
            IReadOnlyList<MediaGalleryItem> images,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IJSObjectReference>(new NoopJsHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopJsHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DefaultDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = propertyName;
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BoundaryLocalizer : IMisskeyLocalizer
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
