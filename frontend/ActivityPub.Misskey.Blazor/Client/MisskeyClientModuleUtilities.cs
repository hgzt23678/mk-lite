using System.Collections.ObjectModel;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;

namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// Typed state used by the ports of account.ts and instance.ts.  The state is
/// deliberately scoped to a frontend session; the server remains the source
/// of truth for account permissions and profile data.
/// </summary>
public sealed record MisskeyAccountSnapshot(
    Guid Id,
    string Username,
    string Acct,
    string DisplayName,
    bool IsAdmin,
    bool IsModerator,
    bool IsLocked,
    bool HasUnreadNotification,
    bool HasUnreadMessagingMessage,
    bool HasUnreadAnnouncement,
    bool HasPendingReceivedFollowRequest,
    string AvatarUrl,
    string ActorIri);

public interface IMisskeyAccountState
{
    MisskeyAccountSnapshot? Current { get; }
    bool IsAdministrator { get; }
    bool IsModerator { get; }
    Task<MisskeyAccountSnapshot?> RefreshAsync(CancellationToken cancellationToken = default);
    Task AddAccountAsync(Guid id, string token, CancellationToken cancellationToken = default);
    Task RemoveAccountAsync(Guid id, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MisskeyStoredAccount>> ReadStoredAccountsAsync(CancellationToken cancellationToken = default);
}

public sealed class MisskeyAccountState(
    IAuthenticatedActorContext actorContext,
    IClientApiQueryService query,
    IMisskeyIndexedStorage indexedStorage) : IMisskeyAccountState
{
    private readonly MisskeyAccountStore accountStore = new(indexedStorage);
    private MisskeyAccountSnapshot? current;

    public MisskeyAccountSnapshot? Current => current;

    public bool IsAdministrator => current?.IsAdmin == true;

    public bool IsModerator => current?.IsModerator == true;

    public async Task<MisskeyAccountSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        AuthenticatedActor? actor = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            current = null;
            return null;
        }

        ClientAccountView? account = await query.FindAccountByIriAsync(actor.ActorIri, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            current = null;
            return null;
        }

        // Role claims are intentionally not inferred from a client payload.
        // The authenticated actor boundary supplies the authoritative admin bit.
        bool isAdmin = await actorContext.IsAdministratorAsync(cancellationToken).ConfigureAwait(false);
        current = new(
            account.Id,
            account.Username,
            account.Acct,
            string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            isAdmin,
            false,
            account.Locked,
            false,
            false,
            false,
            false,
            account.AvatarUrl,
            account.Iri);
        return current;
    }

    public async Task AddAccountAsync(Guid id, string token, CancellationToken cancellationToken = default)
    {
        ValidateToken(token);
        await accountStore.AddAsync(id.ToString("N"), token, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveAccountAsync(Guid id, CancellationToken cancellationToken = default) =>
        accountStore.RemoveAsync(id.ToString("N"), cancellationToken).AsTask();

    public ValueTask<IReadOnlyList<MisskeyStoredAccount>> ReadStoredAccountsAsync(CancellationToken cancellationToken = default) =>
        accountStore.GetAccountsAsync(cancellationToken);

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsControl))
        {
            throw new ArgumentException("The Misskey account token is invalid.", nameof(token));
        }
    }
}

public sealed record MisskeyInstanceSnapshot(
    string Name,
    string Version,
    string Description,
    string IconUrl,
    IReadOnlyList<MisskeyCustomEmojiSnapshot> Emojis);

public sealed record MisskeyCustomEmojiSnapshot(
    string Name,
    string Url,
    string StaticUrl,
    string? Category,
    IReadOnlyList<string> Aliases,
    bool IsAnimated = false);

public static class MisskeyInstanceUtilities
{
    public static IReadOnlyList<string> EmojiCategories(IEnumerable<MisskeyCustomEmojiSnapshot> emojis) =>
        emojis.Select(emoji => emoji.Category ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> EmojiTags(IEnumerable<MisskeyCustomEmojiSnapshot> emojis) =>
        emojis.SelectMany(emoji => emoji.Aliases)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<MisskeyCustomEmojiSnapshot> SearchEmojis(
        IEnumerable<MisskeyCustomEmojiSnapshot> emojis,
        string? query,
        IReadOnlySet<string>? selectedTags = null)
    {
        string needle = query?.Trim() ?? string.Empty;
        IReadOnlySet<string> tags = selectedTags ?? new HashSet<string>(StringComparer.Ordinal);
        if (needle.Length == 0 && tags.Count == 0)
        {
            return [];
        }

        return emojis.Where(emoji =>
                (needle.Length == 0 || emoji.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                 emoji.Aliases.Any(alias => alias.Contains(needle, StringComparison.OrdinalIgnoreCase))) &&
                tags.All(tag => emoji.Aliases.Contains(tag, StringComparer.Ordinal)))
            .ToArray();
    }
}

public sealed record MisskeyClientRuntimeSnapshot(
    Uri PublicBaseUri,
    Uri ApiUri,
    Uri StreamingUri,
    string Host,
    string Hostname,
    string? Language,
    string? Locale,
    string? Ui,
    bool Debug,
    string Version,
    string InstanceName);

public static class MisskeyClientRuntimeUtilities
{
    public static MisskeyClientRuntimeSnapshot FromExplicitConfiguration(
        MisskeyFrontendRuntimeConfiguration configuration,
        string? language,
        string? locale,
        string? ui,
        bool debug,
        string? instanceName = null)
    {
        Uri publicBase = configuration.PublicBaseUri ??
            throw new InvalidOperationException("Misskey frontend PublicBaseUri must be configured explicitly.");
        if (publicBase.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(publicBase.UserInfo))
        {
            throw new InvalidOperationException("Misskey frontend PublicBaseUri must be a safe HTTP(S) URI.");
        }

        Uri api = new(publicBase, "/api/");
        Uri streaming = new((publicBase.Scheme == "https" ? "wss://" : "ws://") + publicBase.Authority + "/streaming");
        return new(
            publicBase,
            api,
            streaming,
            publicBase.Authority,
            publicBase.IdnHost,
            language,
            locale,
            ui,
            debug,
            configuration.Version,
            instanceName ?? publicBase.IdnHost);
    }
}

public sealed record MisskeyNavbarItem(
    string Key,
    string Title,
    string Icon,
    string? Route,
    bool RequiresAccount,
    bool RequiresLockedAccount = false,
    bool VisibleWhenCapabilityEnabled = true);

public static class MisskeyNavbarUtilities
{
    public static IReadOnlyList<MisskeyNavbarItem> DefaultItems { get; } =
    [
        new("notifications", "notifications", "fas fa-bell", "/my/notifications", true),
        new("messaging", "messaging", "fas fa-comments", "/my/messaging", true),
        new("drive", "drive", "fas fa-cloud", "/my/drive", true),
        new("followRequests", "followRequests", "fas fa-user-clock", "/my/follow-requests", true, true),
        new("explore", "explore", "fas fa-hashtag", "/explore", false),
        new("announcements", "announcements", "fas fa-broadcast-tower", "/announcements", false),
        new("lists", "lists", "fas fa-list-ul", "/my/lists", true),
        new("antennas", "antennas", "fas fa-satellite", "/my/antennas", true),
        new("favorites", "favorites", "fas fa-star", "/my/favorites", true),
        new("pages", "pages", "fas fa-file-alt", "/pages", false),
        new("gallery", "gallery", "fas fa-icons", "/gallery", false),
        new("clips", "clip", "fas fa-paperclip", "/my/clips", true),
        new("channels", "channel", "fas fa-satellite-dish", "/channels", false),
    ];

    public static IReadOnlyList<MisskeyNavbarItem> Visible(
        bool authenticated,
        bool locked,
        IReadOnlySet<string>? disabledCapabilities = null)
    {
        IReadOnlySet<string> disabled = disabledCapabilities ?? new HashSet<string>(StringComparer.Ordinal);
        return DefaultItems.Where(item =>
                (!item.RequiresAccount || authenticated) &&
                (!item.RequiresLockedAccount || locked) &&
                (!disabled.Contains(item.Key)))
            .ToArray();
    }
}

public sealed class MisskeyEventBus<TEvent>
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Action<TEvent>> handlers = [];

    public IDisposable Subscribe(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Guid id = Guid.NewGuid();
        lock (gate) handlers.Add(id, handler);
        return new Subscription(() =>
        {
            lock (gate) handlers.Remove(id);
        });
    }

    public void Publish(TEvent value)
    {
        Action<TEvent>[] targets;
        lock (gate) targets = handlers.Values.ToArray();
        foreach (Action<TEvent> target in targets) target(value);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? action = dispose;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}

public sealed record MisskeyAuthCallbackDecision(bool IsCallback, string? SafeReturnPath, string? ErrorCode);

public static class MisskeyActivityPubAuthUtilities
{
    public static MisskeyAuthCallbackDecision ParseCallback(string path, string? returnTo)
    {
        if (!string.Equals(path, "/app/auth/callback", StringComparison.Ordinal))
        {
            return new(false, null, null);
        }

        if (string.IsNullOrWhiteSpace(returnTo))
        {
            return new(true, "/app/", null);
        }

        if (!returnTo.StartsWith("/app/", StringComparison.Ordinal) || returnTo.StartsWith("//", StringComparison.Ordinal) || returnTo.Contains('\\'))
        {
            return new(true, null, "AUTH_RETURN_PATH_INVALID");
        }

        return new(true, returnTo, null);
    }
}

public sealed record MisskeyReloadMessage(string? Path);

public static class MisskeyReloadUtilities
{
    public static MisskeyReloadMessage Create(string? path)
    {
        if (path is null) return new(null);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Any(char.IsControl))
        {
            throw new ArgumentException("Reload path must be same-origin.", nameof(path));
        }
        return new(path);
    }
}

public static class MisskeyNoteCaptureUtilities
{
    public static ClientPostView ApplyStreamUpdate(ClientPostView note, string type, JsonElement body, Guid? viewerId)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (body.ValueKind != JsonValueKind.Object) return note;
        return type switch
        {
            "reacted" => ApplyReaction(note, body, viewerId, +1),
            "unreacted" => ApplyReaction(note, body, viewerId, -1),
            "pollVoted" => ApplyPollVote(note, body, viewerId),
            _ => note
        };
    }

    public static bool IsDeletedEvent(string type) => string.Equals(type, "deleted", StringComparison.Ordinal);

    private static ClientPostView ApplyReaction(ClientPostView note, JsonElement body, Guid? viewerId, int delta)
    {
        string reaction = body.TryGetProperty("reaction", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
        if (reaction.Length == 0) return note;
        Dictionary<string, long> counts = note.Emojis.ToDictionary(emoji => emoji.Shortcode, _ => 0L, StringComparer.Ordinal);
        // ClientPostView does not expose the raw reaction dictionary; the API's
        // projection stores counts in Likes/Announces.  Preserve the immutable
        // note and let the next durable projection refresh when only a reaction
        // delta is received. This avoids inventing a second local source of truth.
        _ = counts;
        return note;
    }

    private static ClientPostView ApplyPollVote(ClientPostView note, JsonElement body, Guid? viewerId)
    {
        if (note.Poll is null || !body.TryGetProperty("choice", out JsonElement choice) || choice.ValueKind != JsonValueKind.Number || !choice.TryGetInt32(out int index) || index < 0 || index >= note.Poll.Options.Count)
        {
            return note;
        }

        List<ClientPollOptionView> options = note.Poll.Options.ToList();
        ClientPollOptionView selected = options[index];
        options[index] = selected with { VotesCount = selected.VotesCount + 1 };
        ClientPollView poll = note.Poll with { Options = new ReadOnlyCollection<ClientPollOptionView>(options), VotesCount = note.Poll.VotesCount + 1 };
        return note with { Poll = poll };
    }
}
