using System.Globalization;
using System.Text.Json;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SubNoteContentTests : BunitContext
{
    [Fact]
    public void PreservesCollapsedBodyLinksDetailsAndAttributeFallthrough()
    {
        Configure();
        NoteViewModel note = Note(
            string.Join('\n', Enumerable.Range(1, 10).Select(index => $"line {index}")),
            contentWarning: null,
            media: [Media()],
            poll: Poll(),
            isHidden: true,
            deletedAt: new DateTimeOffset(2026, 8, 4, 1, 0, 0, TimeSpan.Zero),
            replyId: "reply-note",
            renoteId: "renote-note");

        IRenderedComponent<MkSubNoteContent> component = Render<MkSubNoteContent>(parameters => parameters
            .Add(value => value.Note, note)
            .AddUnmatched("class", "text fixture")
            .AddUnmatched("data-contract", "sub-note"));

        IElement root = component.Find(".wrmlmaau.collapsed.text.fixture");
        Assert.Equal("sub-note", root.GetAttribute("data-contract"));
        Assert.Contains("(private)", root.QuerySelector(":scope > .body")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("(deleted)", root.QuerySelector(":scope > .body")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("notes/reply-note", root.QuerySelector(":scope > .body > .reply")?.GetAttribute("href"));
        Assert.Equal("notes/renote-note", root.QuerySelector(":scope > .body > .rp")?.GetAttribute("href"));
        Assert.NotNull(root.QuerySelector(":scope > .body > .reply > .fa-reply"));
        Assert.Equal("RN: ...", root.QuerySelector(":scope > .body > .rp")?.TextContent);

        IElement[] details = root.QuerySelectorAll(":scope > details").Cast<IElement>().ToArray();
        Assert.Equal(2, details.Length);
        Assert.Equal("(1つのファイル)", details[0].QuerySelector(":scope > summary")?.TextContent.Trim());
        Assert.Equal("アンケート", details[1].QuerySelector(":scope > summary")?.TextContent.Trim());
        Assert.Single(component.FindComponents<MkMediaList>());
        Assert.Single(component.FindComponents<MkPoll>());

        component.Find("button.fade._button").Click();

        Assert.DoesNotContain("collapsed", component.Find(".wrmlmaau").ClassList);
        Assert.Empty(component.FindAll("button.fade._button"));
    }

    [Theory]
    [InlineData(9, 500, null, false)]
    [InlineData(10, 20, null, true)]
    [InlineData(1, 501, null, true)]
    [InlineData(10, 20, "cw", false)]
    public void CollapseThresholdMatchesThePinnedVueInitialization(
        int lineCount,
        int minimumLength,
        string? contentWarning,
        bool expectedCollapsed)
    {
        Configure();
        string text = string.Join('\n', Enumerable.Repeat("x", lineCount));
        text = text.PadRight(minimumLength, 'x');

        IRenderedComponent<MkSubNoteContent> component = Render<MkSubNoteContent>(parameters => parameters
            .Add(value => value.Note, Note(text, contentWarning)));

        Assert.Equal(expectedCollapsed, component.Find(".wrmlmaau").ClassList.Contains("collapsed"));
    }

    [Fact]
    public async Task NoteSimpleDelegatesItsBodyPreservesVShowAndDisposesSizeObservation()
    {
        RecordingElementSizeInterop size = Configure();
        IReadOnlyDictionary<string, string> emojis = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["party"] = "/static-assets/favicon.png"
        };
        NoteViewModel note = Note(
            new string('x', 501),
            contentWarning: "閲覧注意 :party:",
            media: [Media()],
            poll: Poll(),
            emojis: emojis);

        IRenderedComponent<MkNoteSimple> component = Render<MkNoteSimple>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Pinned, true)
            .Add(value => value.CssClass, "component-class")
            .AddUnmatched("class", "fallthrough-class")
            .AddUnmatched("data-contract", "note-simple"));

        component.WaitForAssertion(() => Assert.Equal(1, size.ObserveCalls));
        IElement root = component.Find(".yohlumlk.component-class.fallthrough-class");
        Assert.Equal("note-simple", root.GetAttribute("data-contract"));
        Assert.Equal("true", component.Find("header.kkwtjztg.header").GetAttribute("mini"));
        Assert.Single(component.FindComponents<MkSubNoteContent>());
        Assert.DoesNotContain("collapsed", component.Find(".wrmlmaau.text").ClassList);
        MfmView warning = Assert.Single(
            component.FindComponents<MfmView>().Select(value => value.Instance),
            value => value.Text == note.ContentWarning);
        Assert.Same(note.Author, warning.Author);
        Assert.Same(note.Emojis, warning.CustomEmojis);

        IElement content = component.Find(".yohlumlk > .main > .body > .content");
        Assert.Equal("display: none;", content.GetAttribute("style"));
        component.Find("button.nrvgflfu").Click();
        Assert.Null(component.Find(".yohlumlk > .main > .body > .content").GetAttribute("style"));

        await component.Instance.UpdateElementSize(500, 1280);
        component.WaitForAssertion(() =>
        {
            IElement resized = component.Find(".yohlumlk");
            Assert.Contains("min-width_350px", resized.ClassList);
            Assert.Contains("min-width_500px", resized.ClassList);
        });

        await component.Instance.DisposeAsync();
        Assert.True(size.ObservationToken.IsCancellationRequested);
        Assert.Equal(1, size.Handle.DisposeInvocations);
        Assert.Equal(1, size.Handle.DisposeCalls);
    }

    private RecordingElementSizeInterop Configure()
    {
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example", UriKind.Absolute)));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IClientStorage>(new InMemoryClientStorage());
        Services.AddSingleton<IMediaElementInterop>(new NoOpMediaElementInterop());
        Services.AddSingleton<IMediaGalleryInterop>(new NoOpMediaGalleryInterop());
        Services.AddSingleton<ITimeInterop>(new NoOpTimeInterop());
        Services.AddSingleton<IVisibilityTooltipInterop>(new NoOpVisibilityTooltipInterop());
        Services.AddSingleton<ITimelinePresentationService>(new UnusedTimeline());
        Services.AddSingleton<IAuthenticatedActorContext>(new AnonymousActorContext());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        var size = new RecordingElementSizeInterop();
        Services.AddSingleton<IElementSizeInterop>(size);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return size;
    }

    private static NoteViewModel Note(
        string text,
        string? contentWarning,
        IReadOnlyList<NoteMediaViewModel>? media = null,
        NotePollViewModel? poll = null,
        bool isHidden = false,
        DateTimeOffset? deletedAt = null,
        string? replyId = null,
        string? renoteId = null,
        IReadOnlyDictionary<string, string>? emojis = null) => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "note-id",
            new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
            new NoteAuthorViewModel(
                "alice-id",
                "alice",
                "alice",
                "Alice",
                "/static-assets/user-unknown.png",
                IsBot: false,
                Emojis: emojis),
            text,
            contentWarning,
            (Visibility)Domain.Visibility.Public,
            replyId,
            0,
            0,
            0,
            false,
            new Dictionary<string, long>(StringComparer.Ordinal),
            null,
            media ?? [],
            [],
            [],
            emojis ?? new Dictionary<string, string>(StringComparer.Ordinal),
            poll,
            null,
            IsHidden: isHidden,
            DeletedAt: deletedAt,
            RenoteId: renoteId);

    private static NoteMediaViewModel Media() => new(
        "media-id",
        "application/pdf",
        "/static-assets/favicon.png",
        "/static-assets/favicon.png",
        "document.pdf",
        null,
        null,
        null,
        Sensitive: false);

    private static NotePollViewModel Poll() => new(
        "poll-id",
        null,
        Expired: false,
        Multiple: false,
        VotedByViewer: false,
        OwnVotes: [],
        Options:
        [
            new NotePollOptionViewModel("はい", 1),
            new NotePollOptionViewModel("いいえ", 0)
        ]);

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
            "private" => "private",
            "deleted" => "deleted",
            "showMore" => "もっと見る",
            "poll" => "アンケート",
            "withNFiles" => $"{arguments!["n"]}つのファイル",
            _ => key
        };

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
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

    private sealed class InMemoryClientStorage : IClientStorage
    {
        public ValueTask<T?> ReadAsync<T>(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<T?>(default);
        }

        public ValueTask WriteAsync<T>(
            ClientStorageArea area,
            string key,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(
            ClientStorageArea area,
            string key,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoOpMediaElementInterop : IMediaElementInterop
    {
        public ValueTask<IJSObjectReference> AttachVolumeAsync(
            ElementReference element,
            double initialVolume,
            DotNetObjectReference<MkMediaBanner> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpMediaGalleryInterop : IMediaGalleryInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference gallery,
            IReadOnlyList<MediaGalleryItem> images,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingElementSizeInterop : IElementSizeInterop
    {
        public RecordingHandle Handle { get; } = new();
        public int ObserveCalls { get; private set; }
        public CancellationToken ObservationToken { get; private set; }

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class
        {
            _ = element;
            _ = receiver;
            ObserveCalls++;
            ObservationToken = cancellationToken;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeInvocations { get; private set; }
        public int DisposeCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _ = args;
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
            {
                DisposeInvocations++;
            }
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
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
            _ = element;
            _ = receiver;
            _ = generation;
            _ = unixTimeMilliseconds;
            _ = updateRelativeTime;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpVisibilityTooltipInterop : IVisibilityTooltipInterop
    {
        public ValueTask<IJSObjectReference> AttachTriggerAsync(
            ElementReference target,
            DotNetObjectReference<MkVisibility> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AnonymousActorContext : IAuthenticatedActorContext
    {
        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedActor?>(null);

        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class UnusedTimeline : ITimelinePresentationService
    {
        public Task<TimelinePageViewModel> ReadAsync(
            TimelineKind kind,
            string? beforeId,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoteViewModel> CreateAsync(
            NoteDraft draft,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(
            string noteId,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoteViewModel> ReactAsync(
            string noteId,
            string reaction,
            bool remove,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoteViewModel> VotePollAsync(
            string noteId,
            int choiceIndex,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(
            Guid id,
            TimelineKind kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> MapNoteIdAsync(
            Guid id,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
