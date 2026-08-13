using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class RenoteButtonTests : BunitContext
{
    [Fact]
    public void PublicButtonPreservesUpstreamDomClassesCountAndAttributeFallthrough()
    {
        RecordingRenoteInterop browser = Configure(actor: null);

        IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.Public))
            .Add(value => value.Count, 12L)
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask)
            .AddUnmatched("class", "button")
            .AddUnmatched("data-renote-fixture", "public"));

        AngleSharp.Dom.IElement button = component.Find("button.eddddedb._button.canRenote.button");
        Assert.Null(button.GetAttribute("type"));
        Assert.Equal("public", button.GetAttribute("data-renote-fixture"));
        Assert.Equal("fas fa-retweet", button.QuerySelector(":scope > i")?.ClassName);
        Assert.Equal("12", button.QuerySelector(":scope > p.count")?.TextContent);
        Assert.Equal(1, browser.AttachCalls);
    }

    [Fact]
    public void FollowersOnlyOtherUserShowsTheExactBanBranchWhileOwnNoteCanRenote()
    {
        RecordingRenoteInterop otherBrowser = Configure(new AuthenticatedActor("alice", "https://local.example/users/alice"));
        IRenderedComponent<MkRenoteButton> other = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.FollowersOnly, authorId: "bob-id"))
            .Add(value => value.Count, 4L)
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));

        AngleSharp.Dom.IElement banned = other.Find("button.eddddedb._button:not(.canRenote)");
        Assert.Equal("fas fa-ban", banned.QuerySelector(":scope > i")?.ClassName);
        Assert.Null(banned.QuerySelector(":scope > .count"));
        Assert.Equal(0, otherBrowser.AttachCalls);

        IRenderedComponent<MkRenoteButton> own = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.MentionedOnly, authorId: "alice-id"))
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));

        Assert.NotNull(own.Find("button.eddddedb.canRenote"));
        Assert.Equal(1, otherBrowser.AttachCalls);
    }

    [Fact]
    public async Task AuthenticationMenuRenoteAndQuoteUseTheExistingRealActionPaths()
    {
        Configure(new AuthenticatedActor("alice", "https://local.example/users/alice"));
        MisskeyOverlayService overlays = Services.GetRequiredService<IMisskeyOverlayService>() as MisskeyOverlayService
            ?? throw new InvalidOperationException();
        NoteViewModel note = Note(Visibility.Public);
        NoteViewModel? renoted = null;
        IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Count, 2L)
            .Add(value => value.RenoteRequested, value => { renoted = value; }));

        await component.Find("button").ClickAsync(new MouseEventArgs { Detail = 1 });

        MisskeyOverlayEntry menu = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, menu.Kind);
        Assert.False(menu.OpenedViaKeyboard);
        Assert.Collection(
            menu.MenuItems,
            item => Assert.Equal("Renote", item.Text),
            item => Assert.Equal("引用", item.Text));
        await menu.MenuItems[0].Action!();
        Assert.Same(note, renoted);
        await menu.MenuItems[1].Action!();
        MisskeyOverlayEntry postForm = Assert.Single(
            overlays.Entries,
            entry => entry.Kind == MisskeyOverlayKind.PostForm);
        Assert.Same(note, postForm.PostForm?.Renote);
    }

    [Fact]
    public async Task AnonymousActivationRequiresSignInWithoutCreatingAMutationMenu()
    {
        Configure(actor: null);
        MisskeyOverlayService overlays = Services.GetRequiredService<IMisskeyOverlayService>() as MisskeyOverlayService
            ?? throw new InvalidOperationException();
        IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.Public))
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));

        await component.Find("button").ClickAsync(new MouseEventArgs { Detail = 0 });

        MisskeyOverlayEntry signIn = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.SignIn, signIn.Kind);
        Assert.DoesNotContain(overlays.Entries, entry => entry.Kind == MisskeyOverlayKind.PopupMenu);
    }

    [Fact]
    public async Task TooltipLoadsElevenRealRenoteUsersAndPreservesTheTotalCount()
    {
        IReadOnlyList<NoteAuthorViewModel> users = Enumerable.Range(0, 11)
            .Select(index => User($"user-{index}"))
            .ToArray();
        FixedRenoteDetails details = new(users);
        Configure(actor: null, details: details);
        MisskeyOverlayService overlays = Services.GetRequiredService<IMisskeyOverlayService>() as MisskeyOverlayService
            ?? throw new InvalidOperationException();
        NoteViewModel note = Note(Visibility.Public);
        IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Count, 15L)
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));

        await component.InvokeAsync(component.Instance.ShowRenoteTooltipAsync);

        MisskeyUsersTooltipEntry tooltip = Assert.Single(overlays.UserTooltips);
        Assert.Equal(note.InternalId, details.PostId);
        Assert.Equal(11, details.Limit);
        Assert.Equal(users, tooltip.Users);
        Assert.Equal(15, tooltip.Count);
        Assert.True(tooltip.Showing);

        await component.InvokeAsync(component.Instance.HideRenoteTooltipAsync);
        Assert.False(Assert.Single(overlays.UserTooltips).Showing);
    }

    [Fact]
    public async Task DisposeDuringReplacementAttachKeepsThePendingReceiverAliveAndDiscardsTheLateHandle()
    {
        var browser = new DelayedRenoteInterop();
        Configure(actor: null, browser: browser);
        using IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.Public, id: "9first"))
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));
        Assert.Equal(1, browser.AttachCalls);

        Task rerender = component.InvokeAsync(() => component.Render(parameters => parameters
            .Add(value => value.Note, Note(Visibility.Public, id: "9replacement"))
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask)));
        await browser.WaitForReplacementAttachAsync();

        await component.InvokeAsync(async () => await component.Instance.DisposeAsync());
        browser.CompleteReplacementAttach();
        await browser.WaitForReplacementAttachCompletionAsync();
        await rerender;

        component.WaitForAssertion(() =>
        {
            Assert.True(browser.PendingReceiverWasAlive);
            Assert.NotNull(browser.LateHandle);
            Assert.Equal(1, browser.LateHandle.DisposeInvocations);
            Assert.Equal(1, browser.LateHandle.DisposeCalls);
        });
    }

    [Fact]
    public async Task RepeatedRenderDuringReplacementAttachLeavesOneLiveListener()
    {
        var browser = new DelayedRenoteInterop();
        Configure(actor: null, browser: browser);
        IRenderedComponent<MkRenoteButton> component = Render<MkRenoteButton>(parameters => parameters
            .Add(value => value.Note, Note(Visibility.Public, id: "9first"))
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask));
        NoteViewModel replacement = Note(Visibility.Public, id: "9replacement");

        Task replacementRender = component.InvokeAsync(() => component.Render(parameters => parameters
            .Add(value => value.Note, replacement)
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask)));
        await browser.WaitForReplacementAttachAsync();
        Task repeatedRender = component.InvokeAsync(() => component.Render(parameters => parameters
            .Add(value => value.Note, replacement)
            .Add(value => value.RenoteRequested, _ => Task.CompletedTask)));
        await repeatedRender.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(2, browser.AttachCalls);
        browser.CompleteReplacementAttach();
        await browser.WaitForReplacementAttachCompletionAsync();
        await replacementRender;

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(browser.InitialHandle);
            Assert.Equal(1, browser.InitialHandle.DisposeInvocations);
            Assert.NotNull(browser.LateHandle);
            Assert.Equal(0, browser.LateHandle.DisposeInvocations);
        });

        await component.InvokeAsync(async () => await component.Instance.DisposeAsync());
        Assert.Equal(1, browser.LateHandle!.DisposeInvocations);
        component.Dispose();
    }

    private RecordingRenoteInterop Configure(
        AuthenticatedActor? actor,
        IRenoteDetailsPresentationService? details = null,
        RecordingRenoteInterop? browser = null)
    {
        browser ??= new RecordingRenoteInterop();
        Services.AddSingleton<IRenoteButtonInterop>(browser);
        Services.AddSingleton<IAuthenticatedActorContext>(new FixedActorContext(actor));
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccount(User("alice-id")));
        Services.AddSingleton(details ?? new FixedRenoteDetails([]));
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IMisskeyLocalizer, FixedLocalizer>();
        return browser;
    }

    private static NoteViewModel Note(
        ActivityPub.Misskey.Blazor.Presentation.Visibility visibility,
        string authorId = "bob-id",
        string id = "9renote") => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        id,
        new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
        User(authorId),
        "renote fixture",
        null,
        visibility,
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

    private static NoteAuthorViewModel User(string id) => new(
        id,
        id.Replace("-id", string.Empty, StringComparison.Ordinal),
        id.Replace("-id", string.Empty, StringComparison.Ordinal),
        id,
        "/static-assets/user-unknown.png",
        IsBot: false);

    private sealed class FixedActorContext(AuthenticatedActor? actor) : IAuthenticatedActorContext
    {
        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(actor);

        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
            actor is null
                ? Task.FromException<AuthenticatedActor>(new FrontendAuthenticationException("AUTH_REQUIRED"))
                : Task.FromResult(actor);

        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixedCurrentAccount(NoteAuthorViewModel user) : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(user);
    }

    private sealed class FixedRenoteDetails(IReadOnlyList<NoteAuthorViewModel> users) : IRenoteDetailsPresentationService
    {
        public Guid? PostId { get; private set; }
        public int? Limit { get; private set; }

        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            Guid postId,
            int limit,
            CancellationToken cancellationToken)
        {
            PostId = postId;
            Limit = limit;
            return Task.FromResult(users);
        }
    }

    private class RecordingRenoteInterop : IRenoteButtonInterop
    {
        public int AttachCalls { get; protected set; }

        public virtual ValueTask<IJSObjectReference> AttachAsync(
            ElementReference target,
            DotNetObjectReference<MkRenoteButton> receiver,
            CancellationToken cancellationToken)
        {
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DelayedRenoteInterop : RecordingRenoteInterop
    {
        private readonly TaskCompletionSource replacementAttachStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseReplacementAttach = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource replacementAttachCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool PendingReceiverWasAlive { get; private set; }
        public RecordingHandle? InitialHandle { get; private set; }
        public RecordingHandle? LateHandle { get; private set; }

        public override async ValueTask<IJSObjectReference> AttachAsync(
            ElementReference target,
            DotNetObjectReference<MkRenoteButton> receiver,
            CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            AttachCalls++;
            if (AttachCalls == 1)
            {
                InitialHandle = new RecordingHandle();
                return InitialHandle;
            }

            replacementAttachStarted.TrySetResult();
            await releaseReplacementAttach.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
            try
            {
                _ = receiver.Value;
                PendingReceiverWasAlive = true;
                LateHandle = new RecordingHandle();
                return LateHandle;
            }
            finally
            {
                replacementAttachCompleted.TrySetResult();
            }
        }

        public Task WaitForReplacementAttachAsync() =>
            replacementAttachStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

        public Task WaitForReplacementAttachCompletionAsync() =>
            replacementAttachCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

        public void CompleteReplacementAttach() => releaseReplacementAttach.TrySetResult();
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

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo("ja-JP");
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "renote" => "Renote",
            "quote" => "引用",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => true;
    }
}
