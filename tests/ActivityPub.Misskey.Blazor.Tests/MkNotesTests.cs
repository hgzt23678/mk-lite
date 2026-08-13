using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkNotesTests : BunitContext
{
    public MkNotesTests()
    {
        Services.AddSingleton<IPaginationInterop>(new NoOpPaginationInterop());
        Services.AddSingleton<IDateSeparatedListInterop>(new NoOpDateSeparatedListInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(new NotesLocalizer());
        ComponentFactories.AddStub<NoteView>("<article data-stub-note></article>");
    }

    [Fact]
    public void EmptyBranchPreservesTheMisskeyAssetAndLocalizedNoNotesCopy()
    {
        using IRenderedComponent<MkNotes> component = Render<MkNotes>(parameters => parameters
            .Add(value => value.Source, new NotesSource([]))
            .AddUnmatched("class", "contract-notes"));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("empty contract-notes", component.Find(".empty").ClassName);
            Assert.Equal(
                "/client-assets/about-icon.png",
                component.Find(".empty img._ghost").GetAttribute("src"));
            Assert.Equal("ノートはありません", component.Find(".empty ._fullinfo > div").TextContent);
            Assert.DoesNotContain("giivymft", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ComposesPaginationDateMotionRealNoteProjectionAndAdvertisementSlot()
    {
        NoteViewModel[] notes =
        [
            Note("a", 4),
            Note("b", 3),
            Note("c", 2),
            Note("d", 1)
        ];
        NoteViewModel? replied = null;
        NoteViewModel? renoted = null;
        (NoteViewModel Note, string Reaction)? reacted = null;
        RenderFragment<NoteViewModel> advertisement = note => builder =>
        {
            builder.OpenElement(0, "aside");
            builder.AddAttribute(1, "data-ad-for", note.Id);
            builder.AddContent(2, "real advertisement content");
            builder.CloseElement();
        };

        using IRenderedComponent<MkNotes> component = Render<MkNotes>(parameters => parameters
            .Add(value => value.Source, new NotesSource(notes))
            .Add(value => value.NoGap, true)
            .Add(value => value.AdvertisementContent, advertisement)
            .Add(value => value.ReplyRequested, note => replied = note)
            .Add(value => value.RenoteRequested, note => renoted = note)
            .Add(value => value.ReactionRequested, request => reacted = request));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("giivymft noGap", component.Find(".giivymft").ClassName);
            Assert.Equal("sqadhkmv noGap notes", component.Find(".sqadhkmv").ClassName);
            Assert.Equal("down", component.Find(".sqadhkmv").GetAttribute("data-direction"));
            Assert.Equal("false", component.Find(".sqadhkmv").GetAttribute("data-reversed"));
            Assert.Equal("d", component.Find("[data-ad-for]").GetAttribute("data-ad-for"));
            Assert.Equal(4, component.FindAll("[data-stub-note]").Count);
        });

        IReadOnlyList<IRenderedComponent<Bunit.TestDoubles.Stub<NoteView>>> renderedNotes =
            component.FindComponents<Bunit.TestDoubles.Stub<NoteView>>();
        Assert.Equal(notes.Select(note => note.Id), renderedNotes.Select(rendered =>
            rendered.Instance.Parameters.Get(value => value.Note).Id));
        Assert.All(renderedNotes, rendered => Assert.Equal(
            "qtqtichx",
            rendered.Instance.Parameters.Get(value => value.CssClass)));

        await component.InvokeAsync(() => renderedNotes[0].Instance.Parameters
            .Get(value => value.ReplyRequested)
            .InvokeAsync(notes[0]));
        await component.InvokeAsync(() => renderedNotes[1].Instance.Parameters
            .Get(value => value.RenoteRequested)
            .InvokeAsync(notes[1]));
        await component.InvokeAsync(() => renderedNotes[2].Instance.Parameters
            .Get(value => value.ReactionRequested)
            .InvokeAsync((notes[2], ":party:")));

        Assert.Same(notes[0], replied);
        Assert.Same(notes[1], renoted);
        Assert.Equal((notes[2], ":party:"), reacted);
        Assert.True(component.Instance.Items.Single(note => note.Id == "d").ShouldInsertAdvertisement);
    }

    [Fact]
    public void ReversedPaginationPreservesTheUpDirectionContract()
    {
        NoteViewModel[] notes = [Note("a", 2), Note("b", 1)];

        using IRenderedComponent<MkNotes> component = Render<MkNotes>(parameters => parameters
            .Add(value => value.Source, new NotesSource(notes, reversed: true, markAds: false)));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("up", component.Find(".sqadhkmv").GetAttribute("data-direction"));
            Assert.Equal("true", component.Find(".sqadhkmv").GetAttribute("data-reversed"));
            Assert.Equal(["b", "a"], component.Instance.Items.Select(note => note.Id));
        });
    }

    private static NoteViewModel Note(string id, int minute) => new(
        Guid.NewGuid(),
        id,
        new DateTimeOffset(2026, 8, 4, 12, minute, 0, TimeSpan.Zero),
        new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice",
            "Alice",
            "/static-assets/user-unknown.png",
            IsBot: false),
        $"note {id}",
        ContentWarning: null,
        Visibility.Public,
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
        new Dictionary<string, string>(StringComparer.Ordinal),
        Poll: null,
        Renote: null);

    private sealed class NotesSource(
        IReadOnlyList<NoteViewModel> notes,
        bool reversed = false,
        bool markAds = true) : IMisskeyPaginationSource<NoteViewModel>
    {
        public MisskeyPaginationOptions Options { get; } = new(10, Reversed: reversed);

        public ValueTask<IReadOnlyList<NoteViewModel>> FetchAsync(
            MisskeyPaginationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(notes);
        }

        public string GetId(NoteViewModel item) => item.Id;

        public NoteViewModel MarkAdvertisement(NoteViewModel item) =>
            markAds ? item with { ShouldInsertAdvertisement = true } : item;
    }

    private sealed class NoOpDateSeparatedListInterop : IDateSeparatedListInterop
    {
        public ValueTask<DateSeparatedCalendarPart[]> GetCalendarPartsAsync(
            IReadOnlyList<long> unixTimeMilliseconds,
            CancellationToken cancellationToken) => ValueTask.FromResult(unixTimeMilliseconds
            .Select(value => DateTimeOffset.FromUnixTimeMilliseconds(value))
            .Select(value => new DateSeparatedCalendarPart(value.Month, value.Day))
            .ToArray());

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpPaginationInterop : IPaginationInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            bool enableAutoLoad,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> IsBottomVisibleAsync(
            ElementReference root,
            double tolerance,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
            ElementReference root,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, false, false));

        public ValueTask RestoreScrollAsync(
            ElementReference root,
            PaginationScrollSnapshot snapshot,
            bool stickToBottom,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private sealed class NotesLocalizer : IMisskeyLocalizer
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

        public string Translate(
            string key,
            IReadOnlyDictionary<string, object?>? arguments = null) => key switch
            {
                "noNotes" => "ノートはありません",
                "monthAndDay" => $"{arguments!["month"]}/{arguments["day"]}",
                _ => key
            };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
