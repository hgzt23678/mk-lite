using System.Security.Claims;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Components.Authorization;

namespace ActivityPub.Misskey.Blazor.Tests;

internal sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(principal));

    public static FixedAuthenticationStateProvider Authenticated(string username, params Claim[] additionalClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new("preferred_username", username)
        };
        claims.AddRange(additionalClaims);
        return new(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }
}

internal sealed class StubClientQuery : IClientApiQueryService
{
    public string? LocalActorIri { get; set; }
    public ClientPage<ClientPostView> PublicPage { get; set; } = new([], null, null);
    public ClientPage<ClientPostView> HomePage { get; set; } = new([], null, null);
    public ClientPostView? StreamPost { get; set; }
    public bool CanReceiveStreamEvent { get; set; } = true;
    public string? LastUsername { get; private set; }
    public int StreamPostReads { get; private set; }
    public Guid? LastPostId { get; private set; }
    public string? LastPostViewerActorIri { get; private set; }
    public Dictionary<Guid, ClientAccountView> AccountsById { get; } = [];
    public List<Guid> AccountIdsRead { get; } = [];
    public ClientAccountView? LookupAccount { get; set; }
    public ClientRelationshipView? Relationship { get; set; }

    public ClientReactionSummaryView Reactions { get; set; } = new(
        new Dictionary<string, long>(StringComparer.Ordinal),
        null,
        new Dictionary<string, string>(StringComparer.Ordinal));

    public Task<ClientReactionSummaryView> ReadPostReactionsAsync(
        Guid postId,
        string? viewerActorIri,
        CancellationToken cancellationToken) => Task.FromResult(Reactions);

    public Task<IReadOnlyList<ClientReactionActorView>> ReadPostReactionActorsAsync(
        Guid postId,
        string reaction,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClientReactionActorView>>([]);

    public Task<IReadOnlyList<ClientAnnounceActorView>> ReadPostAnnounceActorsAsync(
        Guid postId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClientAnnounceActorView>>([]);

    public Task<ClientAccountView?> FindAccountByLookupAsync(string account, string localDomain, CancellationToken cancellationToken) =>
        Task.FromResult(LookupAccount);

    public Task<IReadOnlyList<ClientAccountView>> SearchAccountsByUsernameAsync(
        string username,
        string? host,
        int limit,
        string localDomain,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClientAccountView>>(
            string.IsNullOrWhiteSpace(username) ? [] : [LookupAccount ?? Account()]);

    private static ClientAccountView Account()
    {
        DateTimeOffset now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        return new ClientAccountView(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "alice",
            "alice",
            "Alice",
            Locked: false,
            Bot: false,
            Discoverable: true,
            Group: false,
            now,
            string.Empty,
            "https://local.example/@alice",
            "https://local.example/users/alice",
            AvatarUrl: string.Empty,
            HeaderUrl: string.Empty,
            FollowersCount: 0,
            FollowingCount: 0,
            PostsCount: 0,
            LastPostAt: null,
            Emojis: [],
            Fields: []);
    }

    public Task<ClientAccountView?> FindAccountByIdAsync(Guid id, string localDomain, CancellationToken cancellationToken)
    {
        AccountIdsRead.Add(id);
        return Task.FromResult(AccountsById.GetValueOrDefault(id));
    }

    public Task<ClientAccountView?> FindAccountByIriAsync(string actorIri, CancellationToken cancellationToken) =>
        Task.FromResult<ClientAccountView?>(null);

    public Task<string?> FindLocalActorIriAsync(string username, CancellationToken cancellationToken)
    {
        LastUsername = username;
        return Task.FromResult(LocalActorIri);
    }

    public Task<ClientPostView?> FindPostAsync(Guid id, string? viewerActorIri, CancellationToken cancellationToken)
    {
        LastPostId = id;
        LastPostViewerActorIri = viewerActorIri;
        return Task.FromResult(StreamPost);
    }

    public Task<ClientPostView?> FindStreamPostAsync(
        Guid id,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        StreamPostReads++;
        return Task.FromResult(StreamPost);
    }

    public Task<bool> CanReceiveStreamEventAsync(
        StreamEvent streamEvent,
        string? viewerActorIri,
        ClientStreamAudience audience,
        bool localOnly,
        CancellationToken cancellationToken) => Task.FromResult(CanReceiveStreamEvent);

    public Task<ClientPage<ClientPostView>> ReadPublicTimelineAsync(
        Guid? beforeId,
        int limit,
        bool localOnly,
        CancellationToken cancellationToken) => Task.FromResult(PublicPage);

    public Task<ClientPage<ClientPostView>> ReadAccountPostsAsync(
        Guid accountId,
        string localDomain,
        Guid? beforeId,
        int limit,
        string? viewerActorIri,
        CancellationToken cancellationToken) => Task.FromResult(new ClientPage<ClientPostView>([], null, null));

    public Task<ClientPage<ClientPostView>> ReadHomeTimelineAsync(
        string viewerActorIri,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken) => Task.FromResult(HomePage);

    public Task<ClientRelationshipView?> FindRelationshipAsync(
        string ownerActorIri,
        Guid accountId,
        string localDomain,
        CancellationToken cancellationToken) => Task.FromResult(Relationship);

    public Task<ClientPage<ClientFollowRelationView>> ReadFollowRelationsAsync(
        Guid accountId,
        bool followers,
        Guid? beforeId,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ClientPage<ClientFollowRelationView>([], null, null));
}

internal sealed class RecordingClientCommands : IClientApiCommandService
{
    public required ClientPostView Result { get; init; }
    public int CreateCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int ReactCalls { get; private set; }
    public int UndoReactionCalls { get; private set; }
    public int VotePollCalls { get; private set; }
    public string? Username { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? Reaction { get; private set; }
    public int? PollChoice { get; private set; }
    public ClientPostMutation? Mutation { get; private set; }
    public ClientRelationshipView? RelationshipResult { get; set; }
    public int FollowCalls { get; private set; }
    public int UnfollowCalls { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? DeletedPostId { get; private set; }

    public Task<ClientPostView> CreatePostAsync(
        string username,
        string idempotencyKey,
        ClientPostMutation mutation,
        CancellationToken cancellationToken)
    {
        CreateCalls++;
        Username = username;
        IdempotencyKey = idempotencyKey;
        Mutation = mutation;
        return Task.FromResult(Result);
    }

    public Task<ClientPostView> DeletePostAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        Username = username;
        DeletedPostId = postId;
        IdempotencyKey = idempotencyKey;
        return Task.FromResult(Result);
    }
    public Task<ClientPostView> LikeAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedPost();
    public Task<ClientPostView> UndoLikeAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedPost();
    public Task<ClientPostView> ReactAsync(string username, Guid postId, string reaction, string idempotencyKey, CancellationToken cancellationToken)
    {
        ReactCalls++;
        Username = username;
        IdempotencyKey = idempotencyKey;
        Reaction = reaction;
        return Task.FromResult(Result);
    }

    public Task<ClientPostView> UndoReactionAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken)
    {
        UndoReactionCalls++;
        Username = username;
        IdempotencyKey = idempotencyKey;
        return Task.FromResult(Result);
    }
    public Task<ClientPostView> VotePollAsync(string username, Guid postId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken)
    {
        VotePollCalls++;
        Username = username;
        IdempotencyKey = idempotencyKey;
        PollChoice = choiceIndex;
        return Task.FromResult(Result);
    }
    public Task<ClientPostView> AnnounceAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedPost();
    public Task<ClientPostView> UndoAnnounceAsync(string username, Guid postId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedPost();
    public Task<ClientRelationshipView> FollowAsync(string username, Guid accountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        FollowCalls++;
        Username = username;
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
        return Task.FromResult(RelationshipResult ?? throw new NotSupportedException());
    }

    public Task<ClientRelationshipView> UnfollowAsync(string username, Guid accountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        UnfollowCalls++;
        Username = username;
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
        return Task.FromResult(RelationshipResult ?? throw new NotSupportedException());
    }
    public Task<ClientRelationshipView> MuteAsync(string username, Guid accountId, bool hideNotifications, TimeSpan? duration, CancellationToken cancellationToken) => NotUsedRelationship();
    public Task<ClientRelationshipView> UnmuteAsync(string username, Guid accountId, CancellationToken cancellationToken) => NotUsedRelationship();
    public Task<ClientRelationshipView> BlockAsync(string username, Guid accountId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedRelationship();
    public Task<ClientRelationshipView> UnblockAsync(string username, Guid accountId, string idempotencyKey, CancellationToken cancellationToken) => NotUsedRelationship();

    private static Task<ClientPostView> NotUsedPost() => throw new NotSupportedException();
    private static Task<ClientRelationshipView> NotUsedRelationship() => throw new NotSupportedException();
}

internal sealed class InMemoryExternalIds : IExternalEntityIdService
{
    private readonly Dictionary<(ApiDialect Dialect, ExternalEntityType Type, Guid Id), string> byInternal = [];
    private readonly Dictionary<(ApiDialect Dialect, ExternalEntityType Type, string Id), Guid> byExternal = [];

    public Task<string> GetOrCreateAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        Guid internalId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var key = (dialect, entityType, internalId);
        if (!byInternal.TryGetValue(key, out string? value))
        {
            value = entityType.ToString().ToLowerInvariant() + "-" + internalId.ToString("N");
            byInternal[key] = value;
            byExternal[(dialect, entityType, value)] = internalId;
        }

        return Task.FromResult(value);
    }

    public Task<Guid?> ResolveAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        string externalId,
        CancellationToken cancellationToken) =>
        Task.FromResult(byExternal.TryGetValue((dialect, entityType, externalId), out Guid value) ? (Guid?)value : null);

    public async Task<IReadOnlyDictionary<Guid, string>> GetOrCreateManyAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        IReadOnlyCollection<(Guid InternalId, DateTimeOffset Timestamp)> entities,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        foreach ((Guid id, DateTimeOffset timestamp) in entities)
        {
            result[id] = await GetOrCreateAsync(dialect, entityType, id, timestamp, cancellationToken);
        }

        return result;
    }
}

internal static class ClientViewFactory
{
    public static ClientPostView Post(string text = "hello")
    {
        DateTimeOffset now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var account = new ClientAccountView(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "alice",
            "alice",
            "Alice",
            Locked: false,
            Bot: false,
            Discoverable: true,
            Group: false,
            now,
            string.Empty,
            "https://local.example/@alice",
            "https://local.example/users/alice",
            "/media/avatar",
            string.Empty,
            1,
            1,
            1,
            now,
            [],
            []);
        return new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            now,
            null,
            null,
            Sensitive: false,
            string.Empty,
            Visibility.Public,
            "ja",
            "https://local.example/objects/note",
            "https://local.example/notes/note",
            0,
            0,
            0,
            LikedByViewer: false,
            AnnouncedByViewer: false,
            MutedForViewer: false,
            BookmarkedByViewer: false,
            PinnedForViewer: false,
            "<p>hello</p>",
            text,
            "text/x.misskeymarkdown",
            null,
            account,
            [],
            [],
            [],
            [],
            null);
    }
}
