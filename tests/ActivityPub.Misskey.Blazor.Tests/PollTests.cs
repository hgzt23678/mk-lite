using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PollTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnvotedPollPreservesPinnedDomAndTogglesTheAnimatedResultBars()
    {
        _ = Configure(authenticated: true, ResultPoll(single: true));
        IRenderedComponent<MkPoll> component = RenderPoll(OpenPoll(single: true));

        IElement root = component.Find(".tivcixzd");
        Assert.DoesNotContain("done", root.ClassList);
        Assert.Equal(2, root.QuerySelectorAll(":scope > ul > li").Length);
        Assert.All(root.QuerySelectorAll(":scope > ul > li"), choice =>
        {
            Assert.Equal("button", choice.GetAttribute("role"));
            Assert.Equal("0", choice.GetAttribute("tabindex"));
            Assert.Equal("false", choice.GetAttribute("aria-pressed"));
            Assert.Equal("width: 0%", choice.QuerySelector(":scope > .backdrop")?.GetAttribute("style"));
        });
        Assert.Empty(root.QuerySelectorAll(".votes"));
        Assert.Contains("計4票", root.QuerySelector(":scope > p")?.TextContent, StringComparison.Ordinal);
        Assert.Equal("結果を見る", root.QuerySelector(":scope > p > a")?.TextContent);

        root.QuerySelector(":scope > p > a")!.Click();

        Assert.Equal("投票する", component.Find(".tivcixzd > p > a").TextContent);
        Assert.Equal(2, component.FindAll(".tivcixzd .votes").Count);
        Assert.Equal("width: 75%", component.FindAll(".tivcixzd .backdrop")[0].GetAttribute("style"));
        Assert.Equal("width: 25%", component.FindAll(".tivcixzd .backdrop")[1].GetAttribute("style"));
    }

    [Fact]
    public void ConfirmedSingleVoteCallsTheRealPresentationBoundaryOnceAndProjectsViewerState()
    {
        RecordingTimeline timeline = Configure(authenticated: true, ResultPoll(single: true));
        NotePollViewModel? changed = null;
        IRenderedComponent<MkPoll> component = RenderPoll(
            OpenPoll(single: true),
            pollChanged: value => changed = value);

        component.Find(".tivcixzd > ul > li[data-choice-index='1']").Click();
        IRenderedComponent<MkPollDialog> dialog = component.FindComponent<MkPollDialog>();
        Assert.NotNull(dialog.Find(".qzhlnise.dialog > .content > .mk-dialog > .icon.question"));
        Assert.Contains("いいえ", dialog.Markup, StringComparison.Ordinal);

        dialog.Find("button.primary").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.VoteCalls);
            Assert.Equal("note-1", timeline.LastNoteId);
            Assert.Equal(1, timeline.LastChoiceIndex);
            Assert.StartsWith("blazor-poll-vote-", timeline.LastIdempotencyKey, StringComparison.Ordinal);
            Assert.NotNull(changed);
            Assert.Contains("done", component.Find(".tivcixzd").ClassList);
            IElement voted = component.Find(".tivcixzd > ul > li[data-choice-index='1']");
            Assert.Contains("voted", voted.ClassList);
            Assert.Equal("true", voted.GetAttribute("aria-pressed"));
            Assert.NotNull(voted.QuerySelector(":scope > span > i.fas.fa-check"));
            Assert.Equal(2, component.FindAll(".votes").Count);
            Assert.Contains("投票済み", component.Find(".tivcixzd > p").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MultiplePollKeepsOtherChoicesInteractiveAfterACommittedVote()
    {
        RecordingTimeline timeline = Configure(authenticated: true, ResultPoll(single: false));
        IRenderedComponent<MkPoll> component = RenderPoll(OpenPoll(single: false));

        component.Find("li[data-choice-index='0']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        component.FindComponent<MkPollDialog>().Find("button.primary").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, timeline.VoteCalls);
            Assert.DoesNotContain("done", component.Find(".tivcixzd").ClassList);
            Assert.Equal("0", component.Find("li[data-choice-index='1']").GetAttribute("tabindex"));
            Assert.Empty(component.FindAll(".votes"));
            Assert.NotNull(component.Find("li[data-choice-index='0'] i.fa-check"));
        });
    }

    [Fact]
    public void ClosedAndReadOnlyPollsExposeResultsWithoutAnInteractiveFooter()
    {
        _ = Configure(authenticated: true, ResultPoll(single: true));
        NotePollViewModel expired = OpenPoll(single: true) with { Expired = true, ExpiresAt = Now.AddSeconds(-1) };

        IRenderedComponent<MkPoll> closed = RenderPoll(expired);
        Assert.Contains("done", closed.Find(".tivcixzd").ClassList);
        Assert.Contains("終了", closed.Find(".tivcixzd > p").TextContent, StringComparison.Ordinal);
        Assert.All(closed.FindAll(".tivcixzd > ul > li"), choice =>
            Assert.Equal("-1", choice.GetAttribute("tabindex")));
        Assert.Equal(2, closed.FindAll(".votes").Count);

        IRenderedComponent<MkPoll> readOnly = RenderPoll(OpenPoll(single: true), readOnly: true);
        Assert.Empty(readOnly.FindAll(".tivcixzd > p"));
        Assert.Equal(2, readOnly.FindAll(".votes").Count);
    }

    [Fact]
    public void AnonymousVoteOpensSignInWithoutCallingTheMutationBoundary()
    {
        RecordingTimeline timeline = Configure(authenticated: false, ResultPoll(single: true));
        IMisskeyOverlayService overlays = Services.GetRequiredService<IMisskeyOverlayService>();
        IRenderedComponent<MkPoll> component = RenderPoll(OpenPoll(single: true));

        component.Find("li[data-choice-index='0']").Click();

        Assert.Equal(0, timeline.VoteCalls);
        MisskeyOverlayEntry entry = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.SignIn, entry.Kind);
        Assert.Equal("/poll-fixture", entry.Authentication?.ReturnUrl);
    }

    [Fact]
    public void PollUsesPinnedLocaleKeysAndRerendersAfterLocaleSelection()
    {
        _ = Configure(authenticated: true, ResultPoll(single: true));
        IMisskeyLocalizer localizer = Services.GetRequiredService<IMisskeyLocalizer>();
        Assert.True(localizer.TrySelectLocale("en-US"));
        IRenderedComponent<MkPoll> component = RenderPoll(OpenPoll(single: true));

        Assert.Contains("4 votes in total", component.Find(".tivcixzd > p").TextContent, StringComparison.Ordinal);
        Assert.Equal("View results", component.Find(".tivcixzd > p > a").TextContent);
        component.Find(".tivcixzd > p > a").Click();
        Assert.Equal("Vote", component.Find(".tivcixzd > p > a").TextContent);
        Assert.Contains("3 votes", component.FindAll(".tivcixzd .votes")[0].TextContent, StringComparison.Ordinal);

        component.Find("li[data-choice-index='0']").Click();
        IRenderedComponent<MkPollDialog> dialog = component.FindComponent<MkPollDialog>();
        Assert.Contains("Confirm your vote for \"はい\"?", dialog.Find(".body").TextContent, StringComparison.Ordinal);
        Assert.Equal("Poll", dialog.Find("[role='alertdialog']").GetAttribute("aria-label"));
        Assert.Equal("Cancel", dialog.FindAll("button")[1].TextContent.Trim());
    }

    private RecordingTimeline Configure(bool authenticated, NotePollViewModel result)
    {
        var timeline = new RecordingTimeline(result);
        Services.AddSingleton<ITimelinePresentationService>(timeline);
        Services.AddSingleton<IAuthenticatedActorContext>(new FixedActorContext(authenticated));
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        var localeCatalog = new MisskeyLocaleCatalog();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        httpContextAccessor.HttpContext.Request.Headers.AcceptLanguage = "ja-JP";
        Services.AddSingleton<IMisskeyLocalizer>(new MisskeyLocalizer(
            localeCatalog,
            new MisskeyLocaleRequestResolver(localeCatalog),
            httpContextAccessor));
        Services.AddSingleton<IMfmParserInterop, DisconnectedMfmInterop>();
        Services.AddSingleton<IDialogWindowInterop, DisconnectedDialogInterop>();
        Services.AddSingleton<IButtonRippleInterop, DisconnectedButtonInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.GetRequiredService<NavigationManager>().NavigateTo("/poll-fixture");
        return timeline;
    }

    private IRenderedComponent<MkPoll> RenderPoll(
        NotePollViewModel poll,
        bool readOnly = false,
        Action<NotePollViewModel>? pollChanged = null) => Render<MkPoll>(parameters => parameters
            .Add(value => value.NoteId, "note-1")
            .Add(value => value.Poll, poll)
            .Add(value => value.ReadOnly, readOnly)
            .Add(value => value.PollChanged, value => pollChanged?.Invoke(value)));

    private static NotePollViewModel OpenPoll(bool single) => new(
        "poll-1",
        null,
        Expired: false,
        Multiple: !single,
        VotedByViewer: false,
        OwnVotes: [],
        Options:
        [
            new NotePollOptionViewModel("はい", 3),
            new NotePollOptionViewModel("いいえ", 1)
        ]);

    private static NotePollViewModel ResultPoll(bool single) => new(
        "poll-1",
        null,
        Expired: false,
        Multiple: !single,
        VotedByViewer: true,
        OwnVotes: single ? [1] : [0],
        Options:
        [
            new NotePollOptionViewModel("はい", single ? 3 : 4),
            new NotePollOptionViewModel("いいえ", single ? 2 : 1)
        ]);

    private sealed class FixedActorContext(bool authenticated) : IAuthenticatedActorContext
    {
        private static readonly AuthenticatedActor Alice = new("alice", "https://local.example/users/alice");

        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedActor?>(authenticated ? Alice : null);

        public async Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
            await FindAsync(cancellationToken) ?? throw new FrontendAuthenticationException("AUTH_REQUIRED");

        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class RecordingTimeline(NotePollViewModel result) : ITimelinePresentationService
    {
        public int VoteCalls { get; private set; }
        public string? LastNoteId { get; private set; }
        public int? LastChoiceIndex { get; private set; }
        public string? LastIdempotencyKey { get; private set; }

        public Task<NoteViewModel> VotePollAsync(
            string noteId,
            int choiceIndex,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            VoteCalls++;
            LastNoteId = noteId;
            LastChoiceIndex = choiceIndex;
            LastIdempotencyKey = idempotencyKey;
            return Task.FromResult(CreateNote(result));
        }

        public Task<TimelinePageViewModel> ReadAsync(TimelineKind kind, string? beforeId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static NoteViewModel CreateNote(NotePollViewModel poll) => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "note-1",
        Now,
        new NoteAuthorViewModel("alice-id", "alice", "alice", "Alice", "/static-assets/user-unknown.png", false),
        "poll fixture",
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
        poll,
        null);

    private sealed class DisconnectedMfmInterop : IMfmParserInterop, IDisposable
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(string text, bool plain, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<MfmNode>>(new JSDisconnectedException("bUnit has no MFM runtime."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedDialogInterop : IDialogWindowInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class =>
            ValueTask.FromException<IJSObjectReference>(new JSDisconnectedException("bUnit has no dialog runtime."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedButtonInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            ValueTask.FromException<IJSObjectReference>(new JSDisconnectedException("bUnit has no ripple runtime."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
