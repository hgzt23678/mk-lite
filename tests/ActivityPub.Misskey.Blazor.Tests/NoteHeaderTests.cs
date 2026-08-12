using System.Globalization;
using System.Text.Json;
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

public sealed class NoteHeaderTests : BunitContext
{
    [Fact]
    public void PreservesUpstreamHierarchyBotBranchLinksAndAttributeFallthrough()
    {
        Configure();

        IRenderedComponent<MkNoteHeader> component = Render<MkNoteHeader>(parameters => parameters
            .Add(value => value.Note, CreateNote(isBot: true))
            .Add(value => value.Pinned, true)
            .Add(value => value.CssClass, "header")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "note-header"));

        IElement root = component.Find("header.kkwtjztg.header.fixture");
        Assert.Equal("note-header", root.GetAttribute("data-contract"));
        Assert.Equal(4, root.Children.Length);

        IElement name = root.Children[0];
        Assert.Equal("name", name.ClassName);
        Assert.Equal("/@alice", name.GetAttribute("href"));
        Assert.Equal("9alice", name.GetAttribute("data-user-preview"));
        Assert.Equal("Alice", name.TextContent.Trim());

        Assert.Equal("is-bot", root.Children[1].ClassName);
        Assert.Equal("bot", root.Children[1].TextContent);
        Assert.Equal("username", root.Children[2].ClassName);
        Assert.Equal("@alice", root.Children[2].TextContent.Trim());

        IElement info = root.Children[3];
        Assert.Equal("info", info.ClassName);
        IElement createdAt = Assert.IsAssignableFrom<IElement>(info.QuerySelector(":scope > a.created-at"));
        Assert.Equal("/notes/9note", createdAt.GetAttribute("href"));
        Assert.NotNull(createdAt.QuerySelector(":scope > time"));
        Assert.Empty(info.QuerySelectorAll(":scope > span"));

        Assert.DoesNotContain("pinned", root.ClassList);
    }

    [Fact]
    public void OmitsTheBotBadgeForARegularUser()
    {
        Configure();

        IRenderedComponent<MkNoteHeader> component = Render<MkNoteHeader>(parameters => parameters
            .Add(value => value.Note, CreateNote(isBot: false)));

        Assert.Empty(component.FindAll("header.kkwtjztg > .is-bot"));
        Assert.Equal(3, component.Find("header.kkwtjztg").Children.Length);
    }

    private void Configure()
    {
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example", UriKind.Absolute)));
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<ITimeInterop>(new NoOpTimeInterop());
        Services.AddSingleton<IVisibilityTooltipInterop>(new NoOpVisibilityTooltipInterop());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
    }

    private static NoteViewModel CreateNote(bool isBot) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "9note",
        new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
        new NoteAuthorViewModel(
            "9alice",
            "alice",
            "alice",
            "Alice",
            "/static-assets/favicon.png",
            isBot),
        "hello",
        ContentWarning: null,
        ActivityPub.Domain.Visibility.Public,
        ReplyId: null,
        RepliesCount: 0,
        RenotesCount: 0,
        ReactionsCount: 0,
        ReactedByViewer: false,
        new Dictionary<string, long>(StringComparer.Ordinal),
        ViewerReaction: null,
        Media: [],
        Mentions: [],
        Hashtags: [],
        Emojis: new Dictionary<string, string>(StringComparer.Ordinal),
        Poll: null,
        Renote: null);

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MfmNode>>(
                [new("text", JsonSerializer.SerializeToElement(new { text }), null)]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpTimeInterop : ITimeInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<MkTime> receiver,
            long generation,
            long unixTimeMilliseconds,
            bool updateRelativeTime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IJSObjectReference>(new NoOpJsObjectReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpVisibilityTooltipInterop : IVisibilityTooltipInterop
    {
        public ValueTask<IJSObjectReference> AttachTriggerAsync(
            ElementReference target,
            DotNetObjectReference<MkVisibility> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObjectReference());

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObjectReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObjectReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(default(TValue)!);
        }

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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
