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
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PostFormTests : BunitContext
{
    [Fact]
    public void UsesPinnedMisskeyPostFormHierarchyInsteadOfTheTemporaryComposer()
    {
        RecordingTimeline timeline = Configure();

        IRenderedComponent<MkPostForm> component = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Modal, true));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".gafaadew.modal._popup > header > .account > .avatar"));
            Assert.NotNull(component.Find(".gafaadew.modal._popup > .form > textarea.text[data-cy-post-form-text]"));
            Assert.NotNull(component.Find(".gafaadew.modal._popup > .form > footer > button[title='ファイルを添付']"));
            Assert.Null(component.Find("textarea[data-cy-post-form-text]").GetAttribute("maxlength"));
            Assert.Equal("display: none;", component.Find("input.cw").GetAttribute("style"));
            Assert.Equal("display: none;", component.Find("input.hashtags").GetAttribute("style"));
            Assert.DoesNotContain("mk-composer", component.Markup, StringComparison.Ordinal);
            Assert.Equal(0, timeline.CreateCalls);
        });
    }

    [Fact]
    public void PollEditorUsesUpstreamClassesAndCannotMasqueradeAsAPersistedNote()
    {
        RecordingTimeline timeline = Configure();
        IRenderedComponent<MkPostForm> component = Render<MkPostForm>();

        component.Find("button[title='アンケート']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".gafaadew > .form > .zmdxowus"));
            Assert.Equal(2, component.FindAll(".zmdxowus > ul > li").Count);
            Assert.Equal(0, timeline.CreateCalls);
        });
    }

    [Fact]
    public void TextSubmissionInvokesOneApplicationCommandAndReturnsTheCommittedProjection()
    {
        RecordingTimeline timeline = Configure();
        NoteViewModel? posted = null;
        IRenderedComponent<MkPostForm> component = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Posted, note => posted = note));

        component.Find("textarea[data-cy-post-form-text]").Input("Blazorからのノート");
        component.Find("button[data-cy-open-post-form-submit]").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.CreateCalls);
            Assert.Equal("Blazorからのノート", timeline.LastDraft?.Text);
            Assert.Same(timeline.Result, posted);
        });
    }

    [Fact]
    public void InstantPostFormUsesInitialTextAndDoesNotRestoreAnUnrelatedDraft()
    {
        Configure();
        IRenderedComponent<MkPostForm> component = Render<MkPostForm>(parameters => parameters
            .Add(value => value.InitialText, "I $[jelly ❤] #Misskey")
            .Add(value => value.Instant, true));

        component.WaitForAssertion(() => Assert.Equal(
            "I $[jelly ❤] #Misskey",
            component.Find("textarea[data-cy-post-form-text]").GetAttribute("value")));
    }

    [Fact]
    public void PureRenoteUsesOneAnnounceCommandWhileACommentCreatesOneQuote()
    {
        RecordingTimeline timeline = Configure();
        ComponentFactories.AddStub<MkNoteSimple>("<div class=\"preview\"></div>");
        IRenderedComponent<MkPostForm> renote = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Renote, timeline.Result));

        renote.Find("button[data-cy-open-post-form-submit]").Click();

        renote.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.RenoteCalls);
            Assert.Equal(timeline.Result.Id, timeline.LastRenoteId);
            Assert.Equal(0, timeline.CreateCalls);
        });

        IRenderedComponent<MkPostForm> quote = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Renote, timeline.Result));
        quote.Find("textarea[data-cy-post-form-text]").Input("引用コメント");
        quote.Find("button[data-cy-open-post-form-submit]").Click();

        quote.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.RenoteCalls);
            Assert.Equal(1, timeline.CreateCalls);
            Assert.Equal(timeline.Result.InternalId, timeline.LastDraft?.QuoteTargetId);
            Assert.Equal("引用コメント", timeline.LastDraft?.Text);
        });
    }

    [Fact]
    public void SupportedInitialSpecifiedRecipientAndMediaReachTheSharedApplicationDraft()
    {
        RecordingTimeline timeline = Configure();
        ComponentFactories.AddStub<MkPostFormAttach>("<div class=\"file\"></div>");
        var recipient = new NoteAuthorViewModel(
            "bob-id",
            "bob",
            "bob@remote.example",
            "Bob",
            "/static-assets/user-unknown.png",
            IsBot: false);
        var media = new ComposerMediaViewModel(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "image.png",
            "image/png",
            "/media/33333333-3333-3333-3333-333333333333",
            "/media/33333333-3333-3333-3333-333333333333",
            Sensitive: false,
            Description: null,
            Width: 640,
            Height: 480);
        IRenderedComponent<MkPostForm> component = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Specified, recipient)
            .Add(value => value.InitialFiles, [media])
            .Add(value => value.Autofocus, false));

        Assert.Single(component.FindAll(".to-specified > .visibleUsers > span > .mk-acct"));
        Assert.Single(component.FindAll(".attaches > .files > .file"));
        component.Find("button[data-cy-open-post-form-submit]").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.CreateCalls);
            Assert.Equal(Visibility.MentionedOnly, timeline.LastDraft?.Visibility);
            Assert.Equal([media.Id], timeline.LastDraft?.MediaIds);
            Assert.Contains("@bob@remote.example", timeline.LastDraft?.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void UpstreamKeyboardContractSubmitsWithControlEnterAndEmitsEscape()
    {
        RecordingTimeline timeline = Configure();
        int escaped = 0;
        IRenderedComponent<MkPostForm> component = Render<MkPostForm>(parameters => parameters
            .Add(value => value.Escape, () => escaped++));
        AngleSharp.Dom.IElement textarea = component.Find("textarea[data-cy-post-form-text]");
        textarea.Input("ショートカット投稿");

        textarea.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        textarea.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.CreateCalls);
            Assert.Equal(1, escaped);
        });
    }

    private RecordingTimeline Configure()
    {
        var timeline = new RecordingTimeline();
        Services.AddSingleton<ITimelinePresentationService>(timeline);
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccount());
        Services.AddSingleton<IComposerMediaService>(new DisabledComposerMedia());
        Services.AddSingleton<IVisibleUsersPresentationService>(new FixedVisibleUsers());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton(MisskeyFrontendRuntimeConfiguration.Default);
        Services.AddSingleton<IClientStorage>(new InMemoryClientStorage());
        Services.AddScoped<IPostFormInterop, DisconnectedPostFormInterop>();
        Services.AddScoped<IPostFormAttachesInterop, DisconnectedPostFormAttachesInterop>();
        Services.AddScoped<IFormInputInterop, DisconnectedFormInputInterop>();
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        Services.AddSingleton<IMisskeyLocalizer, PostFormLocalizer>();
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        return timeline;
    }

    private sealed class FixedCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
            new NoteAuthorViewModel(
                "alice-id",
                "alice",
                "alice",
                "Alice",
                "/static-assets/user-unknown.png",
                IsBot: false));
    }

    private sealed class RecordingTimeline : ITimelinePresentationService
    {
        public NoteViewModel Result { get; } = CreateNote();
        public int CreateCalls { get; private set; }
        public int RenoteCalls { get; private set; }
        public NoteDraft? LastDraft { get; private set; }
        public string? LastRenoteId { get; private set; }

        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastDraft = draft;
            Assert.StartsWith("blazor-note-", idempotencyKey, StringComparison.Ordinal);
            return Task.FromResult(Result);
        }

        public Task<TimelinePageViewModel> ReadAsync(TimelineKind kind, string? beforeId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken)
        {
            RenoteCalls++;
            LastRenoteId = noteId;
            Assert.StartsWith("blazor-note-", idempotencyKey, StringComparison.Ordinal);
            return Task.FromResult(Result);
        }

        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> VotePollAsync(string noteId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static NoteViewModel CreateNote()
        {
            DateTimeOffset now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
            return new NoteViewModel(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "note-id",
                now,
                new NoteAuthorViewModel("alice-id", "alice", "alice", "Alice", "/static-assets/user-unknown.png", false),
                "Blazorからのノート",
                null,
                Visibility.Public,
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
        }
    }

    private sealed class DisabledComposerMedia : IComposerMediaService
    {
        public Task<ComposerMediaViewModel> UploadAsync(
            string fileName,
            string? declaredMediaType,
            Stream content,
            CancellationToken cancellationToken) => throw new ComposerMediaUnavailableException();
    }

    private sealed class FixedVisibleUsers : IVisibleUsersPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>([]);
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class InMemoryClientStorage : IClientStorage
    {
        public ValueTask<T?> ReadAsync<T>(ClientStorageArea area, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<T?>(default);

        public ValueTask WriteAsync<T>(ClientStorageArea area, string key, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(ClientStorageArea area, string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DisconnectedPostFormInterop : IPostFormInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> ObserveSizeAsync(
            ElementReference root,
            DotNetObjectReference<MkPostForm> receiver,
            CancellationToken cancellationToken) => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask<IJSObjectReference> AttachDropTargetAsync(
            ElementReference root,
            ElementReference input,
            CancellationToken cancellationToken) => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask OpenFilesAsync(ElementReference input, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<string>> CreatePreviewUrlsAsync(ElementReference input, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask InsertTextAsync(ElementReference textarea, string value, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask FocusAsync(ElementReference textarea, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RevokePreviewUrlsAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<AutocompleteContext> GetAutocompleteContextAsync(
            ElementReference textarea,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AutocompleteContext(string.Empty, 0, 0, 0));

        public ValueTask CompleteAutocompleteAsync(
            ElementReference textarea,
            int start,
            int endOffset,
            string replacement,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedPostFormAttachesInterop : IPostFormAttachesInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference files,
            DotNetObjectReference<MkPostFormAttaches> receiver,
            CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedFormInputInterop : IFormInputInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class PostFormLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged;

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo("ja-JP");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "_poll.noOnlyOneChoice" => "選択肢は最低2つ必要です",
            "_poll.choiceN" => $"選択肢{arguments!["n"]}",
            "_poll.noMore" => "これ以上追加できません",
            "_poll.canMultipleVote" => "複数回答可",
            "_poll.expiration" => "期限",
            "_poll.infinite" => "無期限",
            "_poll.at" => "日時指定",
            "_poll.after" => "経過指定",
            "_poll.deadlineDate" => "期日",
            "_poll.deadlineTime" => "時間",
            "_poll.duration" => "期間",
            "_time.second" => "秒",
            "_time.minute" => "分",
            "_time.hour" => "時間",
            "_time.day" => "日",
            "_postForm.quotePlaceholder" => "引用して投稿...",
            "_postForm.replyPlaceholder" => "返信...",
            "_postForm._placeholders.a" => "いまどうしてる？",
            "_postForm._placeholders.b" => "何かあった？",
            "_postForm._placeholders.c" => "何を考えている？",
            "_postForm._placeholders.d" => "言いたいことは？",
            "_postForm._placeholders.e" => "ここに書いてください",
            "_postForm._placeholders.f" => "あなたの声を聞かせてください",
            "cancel" => "キャンセル",
            "switchAccount" => "アカウントを切り替える",
            "visibility" => "公開範囲",
            "previewNoteText" => "プレビュー",
            "recipient" => "宛先",
            "remove" => "削除",
            "add" => "追加",
            "notSpecifiedMentionWarning" => "宛先を指定してください",
            "annotation" => "内容への注釈",
            "hashtags" => "ハッシュタグ",
            "attachFile" => "ファイルを添付",
            "poll" => "アンケート",
            "useCw" => "CWを使用",
            "mention" => "メンション",
            "emoji" => "絵文字",
            "reply" => "返信",
            "quote" => "引用",
            "note" => "ノート",
            "account" => "アカウント",
            "accountSettings" => "アカウント設定",
            "somethingHappened" => "問題が発生しました",
            "gotIt" => "わかった",
            "itsOn" => "オンになっています",
            "itsOff" => "オフになっています",
            _ => key
        };

        public bool TrySelectLocale(string? locale)
        {
            LocaleChanged?.Invoke(this, EventArgs.Empty);
            return string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
        }
    }
}
