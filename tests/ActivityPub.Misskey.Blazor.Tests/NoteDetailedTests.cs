using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NoteDetailedTests : BunitContext
{
    [Fact]
    public async Task OwnRenoteDeletionUsesTheAuthenticatedApplicationCommand()
    {
        NoteViewModel note = Note("renote", string.Empty, Alice());
        var commands = new RecordingClientCommands { Result = ClientViewFactory.Post() };
        var service = new NoteDeletionPresentationService(new FixedActorContext(), commands);

        await service.DeleteAsync(note, "detailed-delete-key", CancellationToken.None);

        Assert.Equal(1, commands.DeleteCalls);
        Assert.Equal("alice", commands.Username);
        Assert.Equal(note.InternalId, commands.DeletedPostId);
        Assert.Equal("detailed-delete-key", commands.IdempotencyKey);
    }

    [Fact]
    public async Task RendersTheDetailedSourceHierarchyAndProjectedThreadWithoutFallbacks()
    {
        TestServices services = Configure();
        NoteViewModel parent = Note("parent", "parent", Bob());
        NoteViewModel ancestor = Note("ancestor", "ancestor", Bob());
        NoteViewModel quote = Note("quote", "quoted", Bob());
        NoteViewModel reply = Note("child", "child", Bob());
        NoteViewModel note = Note("note", "main :party:", Alice() with { IsBot = true }) with
        {
            ContentWarning = "warning",
            ReplyId = parent.Id,
            Reply = parent,
            RepliesCount = 2,
            RenotesCount = 3,
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal) { ["👍"] = 4 },
            Media = [new NoteMediaViewModel("media", "image/png", "/media/file", "/media/preview", "caption", null, 64, 64, false)],
            Poll = new NotePollViewModel("poll", null, false, false, false, [], [new("yes", 2), new("no", 1)]),
            Renote = quote,
            RenoteId = quote.Id,
            IsHidden = true,
            Emojis = new Dictionary<string, string>(StringComparer.Ordinal) { ["party"] = "/media/emoji" }
        };

        NoteViewModel? replied = null;
        using IRenderedComponent<MkNoteDetailed> component = Render<MkNoteDetailed>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Conversation, [ancestor])
            .Add(value => value.Replies, [reply])
            .Add(value => value.CssClass, "contract-detail")
            .AddUnmatched("data-fallthrough", "detailed")
            .Add(value => value.ReplyRequested, value => replied = value));

        IElement root = component.Find(".lxwezrsl");
        Assert.Contains("_block", root.ClassList);
        Assert.Contains("contract-detail", root.ClassList);
        Assert.Equal("detailed", root.GetAttribute("data-fallthrough"));
        Assert.Equal("-1", root.GetAttribute("tabindex"));
        Assert.Equal(3, root.QuerySelectorAll(":scope > .wrpstxzv").Length);
        Assert.Single(root.QuerySelectorAll(":scope > .reply-to-more"));
        Assert.Single(root.QuerySelectorAll(":scope > .reply-to"));
        Assert.Single(root.QuerySelectorAll(":scope > .reply"));
        Assert.Single(root.QuerySelectorAll(":scope > .article > .header"));
        Assert.Single(root.QuerySelectorAll(":scope > .article > .main > .footer > .info > .created-at"));
        Assert.Equal(4, root.QuerySelectorAll(":scope > .article > .main > .footer > .button").Length);
        Assert.Equal("display: none;", root.QuerySelector(":scope > .article > .main > .body > .content")?.GetAttribute("style"));
        Assert.DoesNotContain("url-preview", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("translation", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("channel", component.Markup, StringComparison.Ordinal);

        await component.InvokeAsync(() => component.Instance.HandleNoteHotkeyAsync("toggle-content"));
        Assert.Null(component.Find(".lxwezrsl > .article > .main > .body > .content").GetAttribute("style"));
        component.Find(".lxwezrsl > .article > .main > .footer > button.button").Click();
        Assert.Same(note, replied);

        await component.Instance.UpdateElementSize(299, 1280);
        component.WaitForAssertion(() =>
        {
            Assert.Contains("max-width_500px", component.Find(".lxwezrsl").ClassList);
            Assert.Contains("max-width_450px", component.Find(".lxwezrsl").ClassList);
            Assert.Contains("max-width_350px", component.Find(".lxwezrsl").ClassList);
            Assert.Contains("max-width_300px", component.Find(".lxwezrsl").ClassList);
        });
        Assert.True(services.Size.Observations >= 4);
    }

    [Fact]
    public async Task PreservesMutedDeletedAndOwnRenoteActionsAgainstRealPresentationBoundaries()
    {
        TestServices services = Configure();
        NoteViewModel quoted = Note("quoted", "quoted", Bob()) with { IsMuted = true };
        NoteViewModel renote = Note("renote", string.Empty, Alice()) with
        {
            Renote = quoted,
            RenoteId = quoted.Id
        };

        using IRenderedComponent<MkNoteDetailed> component = Render<MkNoteDetailed>(parameters => parameters
            .Add(value => value.Note, renote));

        IElement muted = component.Find("._panel.muted");
        Assert.Equal("bob-id", muted.QuerySelector("a.name")?.GetAttribute("data-user-preview"));
        Assert.Contains("が何かを言いました", muted.TextContent, StringComparison.Ordinal);
        muted.Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".lxwezrsl.renote")));
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".renote .dropdownIcon")));

        component.Find(".renote .time").Click();
        MisskeyOverlayEntry menu = Assert.Single(services.Overlays.Entries);
        MisskeyMenuItem delete = Assert.Single(menu.MenuItems);
        Assert.True(delete.Danger);
        await component.InvokeAsync(delete.Action!);
        Assert.Same(renote, services.Deletion.Deleted);
        component.WaitForAssertion(() =>
        {
            IElement deleted = component.Find(".lxwezrsl");
            Assert.Equal("display: none;", deleted.GetAttribute("style"));
            Assert.Null(deleted.GetAttribute("tabindex"));
        });
    }

    private TestServices Configure()
    {
        ComponentFactories.AddStub<MkAvatar>();
        ComponentFactories.AddStub<MkUserName>();
        ComponentFactories.AddStub<MkAcct>();
        ComponentFactories.AddStub<MkNoteHeader>();
        ComponentFactories.AddStub<MkSubNoteContent>();
        ComponentFactories.AddStub<MkVisibility>();
        ComponentFactories.AddStub<MfmView>();
        ComponentFactories.AddStub<MkCwButton>();
        ComponentFactories.AddStub<MkMediaList>();
        ComponentFactories.AddStub<MkPoll>();
        ComponentFactories.AddStub<MkNoteSimple>();
        ComponentFactories.AddStub<MkReactionsViewer>();
        ComponentFactories.AddStub<MkTime>();

        var size = new RecordingSize();
        var browser = new RecordingBrowser();
        var overlays = new MisskeyOverlayService();
        var deletion = new RecordingDeletion();
        Services.AddSingleton<IElementSizeInterop>(size);
        Services.AddSingleton<INoteDetailedInterop>(browser);
        Services.AddSingleton<IAuthenticatedActorContext>(new FixedActorContext());
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccount());
        Services.AddSingleton<ITimelinePresentationService>(new UnusedTimeline());
        Services.AddSingleton<INoteDeletionPresentationService>(deletion);
        Services.AddSingleton<IRenoteDetailsPresentationService>(new EmptyRenoteDetails());
        Services.AddSingleton<IRenoteButtonInterop>(new RecordingRenoteButtonBrowser());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return new(size, overlays, deletion);
    }

    private static NoteViewModel Note(string id, string text, NoteAuthorViewModel author) => new(
        Guid.NewGuid(), id, new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero), author, text, null,
        ActivityPub.Domain.Visibility.Public, null, 0, 0, 0, false, new Dictionary<string, long>(StringComparer.Ordinal), null,
        [], [], [], new Dictionary<string, string>(StringComparer.Ordinal), null, null);

    private static NoteAuthorViewModel Alice() => new("alice-id", "alice", "alice", "Alice", "/media/alice", false);
    private static NoteAuthorViewModel Bob() => new("bob-id", "bob", "bob@remote.example", "Bob", "/media/bob", false);

    private sealed record TestServices(RecordingSize Size, MisskeyOverlayService Overlays, RecordingDeletion Deletion);

    private sealed class RecordingSize : IElementSizeInterop
    {
        public int Observations { get; private set; }
        public ValueTask<IJSObjectReference> ObserveAsync<T>(ElementReference element, DotNetObjectReference<T> receiver, CancellationToken cancellationToken) where T : class
        {
            Observations++;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingBrowser : INoteDetailedInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, DotNetObjectReference<MkNoteDetailed> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingRenoteButtonBrowser : IRenoteButtonInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, DotNetObjectReference<MkRenoteButton> receiver, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyRenoteDetails : IRenoteDetailsPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(Guid postId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>([]);
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedActorContext : IAuthenticatedActorContext
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

    private sealed class RecordingDeletion : INoteDeletionPresentationService
    {
        public NoteViewModel? Deleted { get; private set; }
        public Task DeleteAsync(NoteViewModel note, string idempotencyKey, CancellationToken cancellationToken)
        {
            Deleted = note;
            return Task.CompletedTask;
        }
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

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "renotedBy" => $"{arguments!["user"]}がRenote",
            "userSaysSomething" => $"{arguments!["name"]}が何かを言いました",
            "private" => "非公開",
            "reply" => "返信",
            "reaction" => "リアクション",
            "cancelReaction" => "リアクションを取り消す",
            "more" => "もっと！",
            "details" => "詳細",
            "unrenote" => "Renote解除",
            "somethingHappened" => "問題が発生しました",
            "gotIt" => "わかった",
            _ => key
        };
    }
}
