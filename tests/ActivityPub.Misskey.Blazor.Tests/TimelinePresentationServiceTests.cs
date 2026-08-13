using System.Security.Claims;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TimelinePresentationServiceTests
{
    [Fact]
    public async Task ActorContextResolvesTheAuthenticatedUsernameInsteadOfTrustingAnActorClaim()
    {
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var authentication = FixedAuthenticationStateProvider.Authenticated(
            "alice",
            new Claim("actor", "https://attacker.example/users/mallory"));
        var context = new AuthenticatedActorContext(authentication, query);

        AuthenticatedActor actor = await context.RequireAsync(CancellationToken.None);

        Assert.Equal("alice", query.LastUsername);
        Assert.Equal("https://local.example/users/alice", actor.ActorIri);
    }

    [Fact]
    public async Task CreateUsesOneApplicationCommandAndKeepsMisskeySourceFormat()
    {
        ClientPostView post = ClientViewFactory.Post("server-rendered note");
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var commands = new RecordingClientCommands { Result = post };
        var ids = new InMemoryExternalIds();
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(query, commands, ids, context);

        NoteViewModel created = await service.CreateAsync(
            new("server-rendered note", null, ActivityPub.Misskey.Blazor.Presentation.Visibility.Public, null, null, []),
            "blazor-note-idempotency-1",
            CancellationToken.None);

        Assert.Equal(1, commands.CreateCalls);
        Assert.Equal("alice", commands.Username);
        Assert.Equal("blazor-note-idempotency-1", commands.IdempotencyKey);
        Assert.Equal("text/x.misskeymarkdown", commands.Mutation?.SourceFormat);
        Assert.Equal("server-rendered note", created.Text);
    }

    [Fact]
    public async Task InvalidExternalCursorFailsExplicitlyInsteadOfReturningAnEmptyTimeline()
    {
        var query = new StubClientQuery();
        var commands = new RecordingClientCommands { Result = ClientViewFactory.Post() };
        var ids = new InMemoryExternalIds();
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(query, commands, ids, context);

        TimelineCursorException exception = await Assert.ThrowsAsync<TimelineCursorException>(() =>
            service.ReadAsync(TimelineKind.Global, "unknown-cursor", 20, CancellationToken.None));

        Assert.Equal("TIMELINE_CURSOR_INVALID", exception.ErrorCode);
    }

    [Fact]
    public async Task ReactionPreservesTheSelectedMisskeyEmojiInsteadOfCollapsingItToFavourite()
    {
        ClientPostView post = ClientViewFactory.Post();
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/alice",
            StreamPost = post,
            Reactions = new(
                new Dictionary<string, long>(StringComparer.Ordinal) { ["🎉"] = 1 },
                "🎉",
                new Dictionary<string, string>(StringComparer.Ordinal))
        };
        var commands = new RecordingClientCommands { Result = post };
        var ids = new InMemoryExternalIds();
        string noteId = await ids.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            post.Id,
            post.CreatedAt,
            CancellationToken.None);
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(query, commands, ids, context);

        NoteViewModel result = await service.ReactAsync(
            noteId,
            "🎉",
            remove: false,
            "blazor-reaction-idempotency-1",
            CancellationToken.None);

        Assert.Equal(1, commands.ReactCalls);
        Assert.Equal(0, commands.UndoReactionCalls);
        Assert.Equal("🎉", commands.Reaction);
        Assert.Equal("🎉", result.ViewerReaction);
    }

    [Fact]
    public async Task RemovingAReactionUsesTheExactReactionUndoCommand()
    {
        ClientPostView post = ClientViewFactory.Post();
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/alice",
            StreamPost = post
        };
        var commands = new RecordingClientCommands { Result = post };
        var ids = new InMemoryExternalIds();
        string noteId = await ids.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            post.Id,
            post.CreatedAt,
            CancellationToken.None);
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(query, commands, ids, context);

        _ = await service.ReactAsync(
            noteId,
            ":party_parrot:",
            remove: true,
            "blazor-reaction-idempotency-2",
            CancellationToken.None);

        Assert.Equal(0, commands.ReactCalls);
        Assert.Equal(1, commands.UndoReactionCalls);
    }

    [Fact]
    public async Task PollVoteResolvesTheMisskeyIdAndProjectsTheCommittedViewerChoice()
    {
        ClientPostView basePost = ClientViewFactory.Post();
        ClientPostView post = basePost with
        {
            Poll = new ClientPollView(
                basePost.Id,
                DateTimeOffset.UtcNow.AddHours(1),
                Expired: false,
                Multiple: false,
                VotesCount: 1,
                VotersCount: 1,
                VotedByViewer: true,
                OwnVotes: [1],
                Options:
                [
                    new ClientPollOptionView("alpha", 0),
                    new ClientPollOptionView("beta", 1)
                ])
        };
        var query = new StubClientQuery { LocalActorIri = "https://local.example/users/alice" };
        var commands = new RecordingClientCommands { Result = post };
        var ids = new InMemoryExternalIds();
        string noteId = await ids.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            post.Id,
            post.CreatedAt,
            CancellationToken.None);
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(query, commands, ids, context);

        NoteViewModel result = await service.VotePollAsync(
            noteId,
            1,
            "blazor-poll-idempotency-1",
            CancellationToken.None);

        Assert.Equal(1, commands.VotePollCalls);
        Assert.Equal(1, commands.PollChoice);
        Assert.Equal("blazor-poll-idempotency-1", commands.IdempotencyKey);
        Assert.Equal("alice", commands.Username);
        Assert.Collection(result.Poll!.OwnVotes, choice => Assert.Equal(1, choice));
        Assert.True(result.Poll.VotedByViewer.GetValueOrDefault());
        Assert.Equal(1, result.Poll.Options[1].VotesCount);
    }

    [Fact]
    public async Task AuthorizedNoteProjectionPreservesLocalOnlyAndVisibleUserIds()
    {
        ClientPostView original = ClientViewFactory.Post();
        DateTimeOffset createdAt = original.CreatedAt.AddDays(-1);
        var recipient = new ClientAccountView(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "bob",
            "bob@remote.example",
            "Bob",
            Locked: false,
            Bot: false,
            Discoverable: true,
            Group: false,
            createdAt,
            string.Empty,
            "https://remote.example/@bob",
            "https://remote.example/users/bob",
            "/media/proxy/bob/avatar",
            string.Empty,
            0,
            0,
            0,
            null,
            [],
            []);
        ClientPostView post = original with
        {
            Visibility = ActivityPub.Domain.Visibility.MentionedOnly,
            LocalOnly = true,
            VisibleRecipients = [recipient]
        };
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/alice",
            HomePage = new([post], null, null)
        };
        var ids = new InMemoryExternalIds();
        var context = new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query);
        var service = new TimelinePresentationService(
            query,
            new RecordingClientCommands { Result = post },
            ids,
            context);

        TimelinePageViewModel page = await service.ReadAsync(
            TimelineKind.Home,
            null,
            20,
            CancellationToken.None);

        NoteViewModel note = Assert.Single(page.Notes);
        Assert.True(note.LocalOnly);
        string visibleUserId = Assert.Single(note.VisibleUserIds!);
        Guid? resolved = await ids.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            visibleUserId,
            CancellationToken.None);
        Assert.Equal(recipient.Id, resolved);
    }

    [Fact]
    public async Task RenoteProjectionUsesThePersistedTargetMappingInsteadOfInventingAnId()
    {
        ClientPostView target = ClientViewFactory.Post("renoted body") with
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };
        ClientPostView renote = ClientViewFactory.Post(string.Empty) with
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            AnnouncedPost = target
        };
        var query = new StubClientQuery
        {
            PublicPage = new([renote], null, null)
        };
        var ids = new InMemoryExternalIds();
        var context = new AuthenticatedActorContext(
            new FixedAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())),
            query);
        var service = new TimelinePresentationService(
            query,
            new RecordingClientCommands { Result = renote },
            ids,
            context);

        TimelinePageViewModel page = await service.ReadAsync(
            TimelineKind.Global,
            null,
            20,
            CancellationToken.None);

        NoteViewModel projected = Assert.Single(page.Notes);
        Assert.NotNull(projected.Renote);
        Assert.Equal(projected.Renote.Id, projected.RenoteId);
        Guid? resolved = await ids.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            projected.RenoteId!,
            CancellationToken.None);
        Assert.Equal(target.Id, resolved);
    }

    [Fact]
    public async Task ReplyProjectionLoadsTheAuthorizedParentForThePinnedMkNoteHierarchy()
    {
        ClientPostView reply = ClientViewFactory.Post("parent note") with
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666")
        };
        ClientPostView child = ClientViewFactory.Post("child note") with
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            InReplyToId = reply.Id
        };
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/alice",
            HomePage = new([child], null, null),
            StreamPost = reply
        };
        var service = new TimelinePresentationService(
            query,
            new RecordingClientCommands { Result = child },
            new InMemoryExternalIds(),
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("alice"), query));

        TimelinePageViewModel page = await service.ReadAsync(
            TimelineKind.Home,
            null,
            20,
            CancellationToken.None);

        NoteViewModel projected = Assert.Single(page.Notes);
        Assert.Equal("parent note", projected.Reply?.Text);
        Assert.Equal(projected.ReplyId, projected.Reply?.Id);
    }

    [Fact]
    public async Task NotePageLookupUsesViewerAuthorizationAndPreservesTheRemoteCanonicalUrl()
    {
        ClientPostView local = ClientViewFactory.Post("remote note");
        ClientPostView remote = local with
        {
            Url = "https://remote.example/notes/remote-note",
            Iri = "https://remote.example/objects/remote-note",
            Account = local.Account with
            {
                Acct = "alice@remote.example",
                Url = "https://remote.example/@alice",
                Iri = "https://remote.example/users/alice"
            }
        };
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/viewer",
            StreamPost = remote
        };
        var ids = new InMemoryExternalIds();
        string noteId = await ids.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            remote.Id,
            remote.CreatedAt,
            CancellationToken.None);
        var service = new TimelinePresentationService(
            query,
            new RecordingClientCommands { Result = remote },
            ids,
            new AuthenticatedActorContext(FixedAuthenticationStateProvider.Authenticated("viewer"), query));

        NoteViewModel? note = await service.FindAsync(noteId, CancellationToken.None);

        Assert.NotNull(note);
        Assert.Equal(remote.Id, query.LastPostId);
        Assert.Equal("https://local.example/users/viewer", query.LastPostViewerActorIri);
        Assert.Equal("https://remote.example/notes/remote-note", note.RemoteUrl);
    }

    [Fact]
    public async Task VisibleUserLookupUsesTheMisskeyIdMapAndLimitsTheQueryToTen()
    {
        var query = new StubClientQuery();
        var ids = new InMemoryExternalIds();
        DateTimeOffset createdAt = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var externalIds = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            Guid id = Guid.NewGuid();
            query.AccountsById[id] = new ClientAccountView(
                id,
                $"user{index}",
                $"user{index}@remote.example",
                $"User {index}",
                Locked: false,
                Bot: false,
                Discoverable: true,
                Group: false,
                createdAt.AddMinutes(index),
                string.Empty,
                $"https://remote.example/@user{index}",
                $"https://remote.example/users/user{index}",
                $"/media/proxy/user{index}/avatar",
                string.Empty,
                0,
                0,
                0,
                null,
                [],
                []);
            externalIds.Add(await ids.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                id,
                createdAt.AddMinutes(index),
                CancellationToken.None));
        }
        var service = new VisibleUsersPresentationService(
            query,
            ids,
            new MisskeyFrontendRuntimeConfiguration(
                MisskeyFrontendRuntimeConfiguration.PortVersion,
                null,
                new Uri("https://local.example")));

        IReadOnlyList<NoteAuthorViewModel> users = await service.ReadAsync(externalIds, CancellationToken.None);

        Assert.Equal(VisibleUsersPresentationService.MaximumUsers, users.Count);
        Assert.Equal(externalIds.Take(10), users.Select(user => user.Id));
        Assert.Equal(10, query.AccountIdsRead.Count);
    }
}
