using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotePageTests : BunitContext
{
    [Fact]
    public void PreservesTheV12PageHierarchyRemoteCautionAndBackendSupportedNoteOnly()
    {
        NoteViewModel note = Note();
        var presentation = new RecordingNotePagePresentation([note]);
        Configure(presentation);

        using IRenderedComponent<NotePage> component = Render<NotePage>(parameters => parameters
            .Add(page => page.NoteId, note.Id));

        component.WaitForAssertion(() => Assert.Equal(
            "loaded",
            component.Find(".fcuexfpr").GetAttribute("data-note-page-state")));
        Assert.NotNull(component.Find(".fcuexfpr > .note > .main._gap > .note._gap"));
        Assert.Equal([note.Id], presentation.NoteIds);
        Assert.Equal(800, component.FindComponent<MkSpacer>().Instance.ContentMax);

        MkPageHeaderMetadata metadata = Assert.IsType<MkPageHeaderMetadata>(
            component.FindComponent<MkPageHeader>().Instance.Metadata);
        Assert.Equal("Note", metadata.Title);
        Assert.Equal(note.Author, metadata.Avatar);

        Bunit.TestDoubles.Stub<MkRemoteCaution> caution =
            component.FindComponent<Bunit.TestDoubles.Stub<MkRemoteCaution>>().Instance;
        Assert.Equal(note.RemoteUrl, caution.Parameters.Get(value => value.Href));
        Bunit.TestDoubles.Stub<MkNoteDetailed> detailed =
            component.FindComponent<Bunit.TestDoubles.Stub<MkNoteDetailed>>().Instance;
        Assert.Equal(note, detailed.Parameters.Get(value => value.Note));
        Assert.Equal("note", detailed.Parameters["class"]);

        Assert.Empty(component.FindAll(".load"));
        Assert.Empty(component.FindAll(".clips"));
    }

    [Fact]
    public async Task FailureShowsThePinnedRetryStateAndRetriesTheRealLookup()
    {
        NoteViewModel note = Note();
        var presentation = new RecordingNotePagePresentation(
        [
            new InvalidOperationException("database unavailable"),
            note
        ]);
        Configure(presentation);

        using IRenderedComponent<NotePage> component = Render<NotePage>(parameters => parameters
            .Add(page => page.NoteId, note.Id));

        component.WaitForAssertion(() => Assert.Equal(
            "NOTE_LOAD_FAILED",
            component.Find(".fcuexfpr").GetAttribute("data-error-code")));
        Bunit.TestDoubles.Stub<MkError> error =
            component.FindComponent<Bunit.TestDoubles.Stub<MkError>>().Instance;
        await component.InvokeAsync(() => error.Parameters.Get(value => value.Retry).InvokeAsync());

        component.WaitForAssertion(() => Assert.Equal(
            "loaded",
            component.Find(".fcuexfpr").GetAttribute("data-note-page-state")));
        Assert.Equal([note.Id, note.Id], presentation.NoteIds);
    }

    private void Configure(INotePagePresentationService presentation)
    {
        var browser = new NoOpBrowser();
        Services.AddLogging();
        Services.AddSingleton(presentation);
        Services.AddSingleton<INotePageInterop>(browser);
        Services.AddSingleton<IStickyContainerInterop>(browser);
        Services.AddSingleton<ISpacerInterop>(browser);
        Services.AddSingleton<IPageHeaderInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new AnimationDisabledDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(new NotePageLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<ICurrentAccountPresentationService>(new CurrentAccount(Note().Author));
        ComponentFactories.AddStub<MkNoteDetailed>();
        ComponentFactories.AddStub<MkRemoteCaution>();
        ComponentFactories.AddStub<MkLoading>();
        ComponentFactories.AddStub<MkError>();
    }

    private static NoteViewModel Note() => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "9notepage",
        new DateTimeOffset(2026, 8, 4, 12, 34, 0, TimeSpan.Zero),
        new(
            "9remoteauthor",
            "alice",
            "alice@remote.example",
            "Alice",
            "/static-assets/user-unknown.png",
            IsBot: false),
        "Misskey v12 note page",
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
        Renote: null,
        RemoteUrl: "https://remote.example/notes/9notepage");

    private sealed class RecordingNotePagePresentation(IEnumerable<object> results) : INotePagePresentationService
    {
        private readonly Queue<object> results = new(results);

        public List<string> NoteIds { get; } = [];

        public Task<NoteViewModel?> FindAsync(string noteId, CancellationToken cancellationToken)
        {
            NoteIds.Add(noteId);
            object result = results.Dequeue();
            return result is Exception exception
                ? Task.FromException<NoteViewModel?>(exception)
                : Task.FromResult((NoteViewModel?)result);
        }
    }

    private sealed class NotePageLocalizer : IMisskeyLocalizer
    {
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public event EventHandler? LocaleChanged { add { } remove { } }

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "note" => "Note",
            _ => key
        };

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AnimationDisabledDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(propertyName == "animation" && typeof(T) == typeof(bool)
                ? (T)(object)false
                : fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CurrentAccount(NoteAuthorViewModel account) : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(account);
    }

    private sealed class NoOpBrowser :
        INotePageInterop,
        IStickyContainerInterop,
        ISpacerInterop,
        IPageHeaderInterop,
        IJSObjectReference
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference container,
            DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
            long generation,
            string phase,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
