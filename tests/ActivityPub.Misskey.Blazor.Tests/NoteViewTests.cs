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

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NoteViewTests : BunitContext
{
    [Fact]
    public void PreservesPinnedReplyCwQuoteMediaPollAndFooterHierarchy()
    {
        TestServices services = Configure();
        NoteViewModel reply = Note("reply-id", "reply preview", Bob());
        NoteViewModel quote = Note("quote-id", "quoted note", Bob());
        NoteViewModel note = Note("note-id", "main note", Alice()) with
        {
            ContentWarning = "閲覧注意",
            ReplyId = reply.Id,
            Reply = reply,
            RepliesCount = 2,
            RenotesCount = 3,
            ReactionsCount = 4,
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal) { ["👍"] = 4 },
            Media = [Media()],
            Poll = Poll(),
            Renote = quote,
            RenoteId = quote.Id,
            IsHidden = true
        };

        using IRenderedComponent<NoteView> component = Render<NoteView>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Pinned, true)
            .Add(value => value.CssClass, "component-class")
            .AddUnmatched("class", "fallthrough-class")
            .AddUnmatched("data-contract", "note-view"));

        IElement root = component.Find(".tkcbzcuz.component-class.fallthrough-class");
        Assert.Equal("note-view", root.GetAttribute("data-contract"));
        Assert.NotNull(root.QuerySelector(":scope > .reply-to.wrpstxzv"));
        Assert.Equal("ピン留めされたノート", root.QuerySelector(":scope > .info")?.TextContent.Trim());
        Assert.NotNull(root.QuerySelector(":scope > .article > .avatar.eiwwqkts"));
        Assert.NotNull(root.QuerySelector(":scope > .article > .main > .header.kkwtjztg"));
        Assert.Equal("display: none;", root.QuerySelector(":scope > .article > .main > .body > .content")?.GetAttribute("style"));

        component.Find("button.nrvgflfu").Click();

        IElement content = component.Find(".tkcbzcuz > .article > .main > .body > .content");
        Assert.Null(content.GetAttribute("style"));
        Assert.Contains("(非公開)", content.QuerySelector(":scope > .text")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("notes/reply-id", content.QuerySelector(":scope > .text > .reply")?.GetAttribute("href"));
        Assert.Equal("RN:", content.QuerySelector(":scope > .text > .rp")?.TextContent);
        Assert.NotNull(content.QuerySelector(":scope > .files > .hoawjimk"));
        Assert.NotNull(content.QuerySelector(":scope > .poll"));
        Assert.NotNull(content.QuerySelector(":scope > .renote > .yohlumlk"));
        Assert.Single(component.FindComponents<MkNoteSub>());
        Assert.Single(component.FindComponents<MkNoteSimple>());

        IElement footer = component.Find(".tkcbzcuz > .article > .main > .footer");
        Assert.NotNull(footer.QuerySelector(":scope > .tdflqwzn"));
        Assert.Equal(4, footer.QuerySelectorAll(":scope > .button").Length);
        Assert.NotNull(footer.QuerySelector(":scope > .button > .fa-reply-all"));
        Assert.Null(footer.QuerySelector(":scope > .button.reacted"));
        Assert.Equal(2, services.Size.Observations);
    }

    [Fact]
    public async Task HotkeysUseTheRealReplyReactionRenoteAndMenuBoundaries()
    {
        TestServices services = Configure();
        string? reaction = null;
        int renotes = 0;
        NoteViewModel note = Note("note-id", "main note", Alice());

        using IRenderedComponent<NoteView> component = Render<NoteView>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.ReactionRequested, request => reaction = request.Reaction)
            .Add(value => value.RenoteRequested, _ => renotes++));

        await component.InvokeAsync(() => component.Instance.HandleNoteHotkeyAsync("reply"));
        MisskeyOverlayEntry replyOverlay = Assert.Single(services.Overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PostForm, replyOverlay.Kind);
        Assert.Same(note, replyOverlay.PostForm?.Reply);
        services.Overlays.Close(replyOverlay.Id);

        await component.InvokeAsync(() => component.Instance.HandleNoteHotkeyAsync("react"));
        MisskeyEmojiPickerEntry picker = Assert.Single(services.Overlays.EmojiPickers);
        Assert.True(picker.AsReactionPicker);
        await component.InvokeAsync(() => picker.Chosen("🎉"));
        Assert.Equal("🎉", reaction);
        services.Overlays.Close(picker.Id);

        await component.InvokeAsync(() => component.Instance.HandleNoteHotkeyAsync("renote"));
        MisskeyOverlayEntry renoteMenu = Assert.Single(services.Overlays.Entries);
        Assert.True(renoteMenu.OpenedViaKeyboard);
        Assert.Equal("Renote", renoteMenu.MenuItems[0].Text);
        await component.InvokeAsync(() => renoteMenu.MenuItems[0].Action!());
        Assert.Equal(1, renotes);
        services.Overlays.Close(renoteMenu.Id);

        await component.InvokeAsync(() => component.Instance.HandleNoteHotkeyAsync("menu"));
        MisskeyOverlayEntry noteMenu = Assert.Single(services.Overlays.Entries);
        Assert.True(noteMenu.OpenedViaKeyboard);
        Assert.Equal(["詳細", "返信"], noteMenu.MenuItems.Select(item => item.Text));
    }

    [Fact]
    public async Task PreservesExpansionAcrossStreamUpdatesAndAppliesDeleteAndDisposalState()
    {
        TestServices services = Configure();
        NoteViewModel note = Note("note-id", new string('x', 501), Alice());
        IRenderedComponent<NoteView> component = Render<NoteView>(parameters => parameters
            .Add(value => value.Note, note));

        Assert.Contains("collapsed", component.Find(".content").ClassList);
        component.Find("button.fade").Click();
        Assert.DoesNotContain("collapsed", component.Find(".content").ClassList);

        component.Render(parameters => parameters.Add(value => value.Note, note with { ReactionsCount = 8 }));
        Assert.DoesNotContain("collapsed", component.Find(".content").ClassList);

        Assert.DoesNotContain(
            component.Find(".tkcbzcuz").ClassList,
            value => value.StartsWith("max-width_", StringComparison.Ordinal));

        component.Render(parameters => parameters.Add(value => value.Note, note with
        {
            DeletedAt = new DateTimeOffset(2026, 8, 4, 13, 0, 0, TimeSpan.Zero)
        }));
        IElement deleted = component.Find(".tkcbzcuz");
        Assert.Equal("display: none;", deleted.GetAttribute("style"));
        Assert.Null(deleted.GetAttribute("tabindex"));

        await component.Instance.DisposeAsync();
        Assert.Equal(0, services.Size.Observations);
        Assert.Equal(1, services.NoteBehavior.Handle.DisposeInvocations);
        Assert.Equal(1, services.NoteBehavior.Handle.DisposeCalls);
    }

    private TestServices Configure()
    {
        var size = new RecordingElementSizeInterop();
        var noteBehavior = new RecordingNoteViewInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example", UriKind.Absolute)));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IClientStorage>(new InMemoryClientStorage());
        Services.AddSingleton<IElementSizeInterop>(size);
        Services.AddSingleton<INoteViewInterop>(noteBehavior);
        Services.AddSingleton<IMediaElementInterop>(new NoOpMediaElementInterop());
        Services.AddSingleton<IMediaGalleryInterop>(new NoOpMediaGalleryInterop());
        Services.AddSingleton<IBlurhashImageInterop>(new NoOpBlurhashImageInterop());
        Services.AddSingleton<ITimeInterop>(new NoOpTimeInterop());
        Services.AddSingleton<IVisibilityTooltipInterop>(new NoOpVisibilityTooltipInterop());
        Services.AddSingleton<IReactionViewerInterop>(new NoOpReactionViewerInterop());
        Services.AddSingleton<IRenoteButtonInterop>(new NoOpRenoteButtonInterop());
        Services.AddSingleton<IReactionDetailsPresentationService>(new EmptyReactionDetails());
        Services.AddSingleton<IRenoteDetailsPresentationService>(new EmptyRenoteDetails());
        Services.AddSingleton<IAuthenticatedActorContext>(new AuthenticatedActorContextStub());
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccount());
        Services.AddSingleton<ITimelinePresentationService>(new UnusedTimeline());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return new(size, noteBehavior, overlays);
    }

    private static NoteViewModel Note(string id, string text, NoteAuthorViewModel author) => new(
        Guid.Parse(id == "note-id" ? "11111111-1111-1111-1111-111111111111" : Guid.NewGuid().ToString()),
        id,
        new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
        author,
        text,
        null,
        ActivityPub.Misskey.Blazor.Presentation.Visibility.Public,
        null,
        0,
        0,
        0,
        false,
        new Dictionary<string, long>(StringComparer.Ordinal),
        null,
        [],
        [],
        [],
        new Dictionary<string, string>(StringComparer.Ordinal),
        null,
        null);

    private static NoteAuthorViewModel Alice() => new(
        "alice-id", "alice", "alice", "Alice", "/static-assets/user-unknown.png", IsBot: false);

    private static NoteAuthorViewModel Bob() => new(
        "bob-id", "bob", "bob@remote.example", "Bob", "/static-assets/user-unknown.png", IsBot: false);

    private static NoteMediaViewModel Media() => new(
        "media-id", "image/png", "/static-assets/favicon.png", "/static-assets/favicon.png",
        "image", null, 64, 64, Sensitive: false);

    private static NotePollViewModel Poll() => new(
        "poll-id", null, Expired: false, Multiple: false, VotedByViewer: false, OwnVotes: [],
        Options: [new NotePollOptionViewModel("yes", 2), new NotePollOptionViewModel("no", 1)]);

    private sealed record TestServices(
        RecordingElementSizeInterop Size,
        RecordingNoteViewInterop NoteBehavior,
        MisskeyOverlayService Overlays);

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "pinnedNote" => "ピン留めされたノート",
            "renotedBy" => $"{arguments!["user"]}がRenote",
            "private" => "非公開",
            "reply" => "返信",
            "reaction" => "リアクション",
            "cancelReaction" => "リアクションを取り消す",
            "more" => "もっと！",
            "details" => "詳細",
            "renote" => "Renote",
            "quote" => "引用",
            "showMore" => "もっと見る",
            "showLess" => "閉じる",
            "somethingHappened" => "問題が発生しました",
            "gotIt" => "わかった",
            "_poll.vote" => "投票",
            "_poll.showResult" => "結果を見る",
            "_poll.voted" => "投票済み",
            "_poll.closed" => "終了",
            "processing" => "処理中",
            _ => key
        };
        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(string text, bool plain, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MfmNode>>([new("text", JsonSerializer.SerializeToElement(new { text }), null)]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);
        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class InMemoryClientStorage : IClientStorage
    {
        public ValueTask<T?> ReadAsync<T>(ClientStorageArea area, string key, CancellationToken cancellationToken = default) => ValueTask.FromResult<T?>(default);
        public ValueTask WriteAsync<T>(ClientStorageArea area, string key, T value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(ClientStorageArea area, string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingElementSizeInterop : IElementSizeInterop
    {
        public int Observations { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public ValueTask<IJSObjectReference> ObserveAsync<T>(ElementReference element, DotNetObjectReference<T> receiver, CancellationToken cancellationToken) where T : class
        {
            Observations++;
            LastToken = cancellationToken;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingNoteViewInterop : INoteViewInterop
    {
        public RecordingHandle Handle { get; } = new();
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, DotNetObjectReference<NoteView> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(Handle);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpMediaElementInterop : IMediaElementInterop
    {
        public ValueTask<IJSObjectReference> AttachVolumeAsync(ElementReference element, double initialVolume, DotNetObjectReference<MkMediaBanner> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpMediaGalleryInterop : IMediaGalleryInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference gallery, IReadOnlyList<MediaGalleryItem> images, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpBlurhashImageInterop : IBlurhashImageInterop
    {
        public ValueTask<bool> DrawAsync(ElementReference canvas, ElementReference image, string? hash, int size, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpTimeInterop : ITimeInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, DotNetObjectReference<MkTime> receiver, long generation, long unixTimeMilliseconds, bool updateRelativeTime, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpVisibilityTooltipInterop : IVisibilityTooltipInterop
    {
        public ValueTask<IJSObjectReference> AttachTriggerAsync(ElementReference target, DotNetObjectReference<MkVisibility> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask<IJSObjectReference> AttachTooltipAsync(ElementReference target, ElementReference tooltip, DotNetObjectReference<MkTooltip> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpReactionViewerInterop : IReactionViewerInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference target, DotNetObjectReference<MkReactionsViewerReaction> receiver, bool canToggle, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpRenoteButtonInterop : IRenoteButtonInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference target, DotNetObjectReference<MkRenoteButton> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyReactionDetails : IReactionDetailsPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(Guid postId, string reaction, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>([]);
    }

    private sealed class EmptyRenoteDetails : IRenoteDetailsPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(Guid postId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>([]);
    }

    private sealed class AuthenticatedActorContextStub : IAuthenticatedActorContext
    {
        private static readonly AuthenticatedActor Actor = new("alice", "https://local.example/users/alice");
        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) => Task.FromResult<AuthenticatedActor?>(Actor);
        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) => Task.FromResult(Actor);
        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixedCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Alice());
    }

    private sealed class UnusedTimeline : ITimelinePresentationService
    {
        public Task<TimelinePageViewModel> ReadAsync(TimelineKind kind, string? beforeId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NoteViewModel> VotePollAsync(string noteId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeInvocations { get; private set; }
        public int DisposeCalls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (string.Equals(identifier, "dispose", StringComparison.Ordinal)) DisposeInvocations++;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
