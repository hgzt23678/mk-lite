using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.State;

namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record AutocompleteUserViewModel(
    string Id,
    string Username,
    string? Host,
    string DisplayName,
    string AvatarUrl);

public sealed record AutocompleteEmojiViewModel(
    string Emoji,
    string Name,
    string? AliasOf,
    string? Url,
    bool IsCustomEmoji);

public interface IAutocompletePresentationService
{
    Task<IReadOnlyList<AutocompleteUserViewModel>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SearchHashtagsAsync(
        string query,
        CancellationToken cancellationToken);

    IReadOnlyList<AutocompleteEmojiViewModel> SearchEmojis(string? query);

    IReadOnlyList<string> SearchMfmTags(string? query);

    void RememberEmoji(string emoji);
}

public sealed class AutocompletePresentationService(
    IClientApiQueryService clientQuery,
    IHashtagRepository hashtags,
    IEmojiCatalog emojiCatalog,
    MisskeyFrontendRuntimeConfiguration runtime) : IAutocompletePresentationService
{
    private const int MaxEmojiSuggestions = 30;
    private const int MaxRecentEmojis = 32;
    private static readonly string[] MfmTags =
    [
        "tada", "confetti", "party", "ticker", "jelly", "jelly-in", "jelly-out", "jelly-bounce",
        "spin", "spin-x", "spin-y", "spin-3d", "pulse", "blink", "pop", "jump", "ruby", "rainbow",
        "flip", "flip-h", "flip-v", "bounce", "shake", "twitch", "shake-r", "shake-b", "shake-t",
        "shake-l", "rotate", "font", "fg", "bg", "border", "position", "small", "center", "blur",
        "italic", "url", "scale", "x2", "x3", "x4", "huge", "plain", "motion", "bordered", "sparkle"
    ];

    private readonly List<AutocompleteEmojiViewModel> emojiDb = BuildEmojiDatabase(emojiCatalog);

    public async Task<IReadOnlyList<AutocompleteUserViewModel>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        IReadOnlyList<ClientAccountView> accounts = await clientQuery.SearchAccountsByUsernameAsync(
            query,
            null,
            10,
            runtime.PublicBaseUri?.IdnHost ?? "local.example",
            cancellationToken).ConfigureAwait(false);
        return accounts.Select(account => new AutocompleteUserViewModel(
            account.Id.ToString("D"),
            account.Username,
            account.Acct.Contains('@', StringComparison.Ordinal)
                ? account.Acct[(account.Acct.IndexOf('@') + 1)..]
                : null,
            account.DisplayName,
            account.AvatarUrl)).ToArray();
    }

    public async Task<IReadOnlyList<string>> SearchHashtagsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return await hashtags.SearchAsync(query, 30, 0, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<AutocompleteEmojiViewModel> SearchEmojis(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string q = query;
        var matched = new List<AutocompleteEmojiViewModel>(MaxEmojiSuggestions);
        AddMatches(emojiDb, q, matched, allowAliases: false);
        if (matched.Count < MaxEmojiSuggestions)
        {
            AddMatches(emojiDb, q, matched, allowAliases: true);
        }

        if (matched.Count < MaxEmojiSuggestions)
        {
            foreach (AutocompleteEmojiViewModel emoji in emojiDb)
            {
                if (matched.Count >= MaxEmojiSuggestions)
                {
                    break;
                }

                if (emoji.Name.Contains(q, StringComparison.Ordinal) &&
                    !matched.Any(item => string.Equals(item.Emoji, emoji.Emoji, StringComparison.Ordinal)))
                {
                    matched.Add(emoji);
                }
            }
        }

        return matched;
    }

    public IReadOnlyList<string> SearchMfmTags(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return MfmTags;
        }

        return MfmTags.Where(tag => tag.StartsWith(query, StringComparison.Ordinal)).ToArray();
    }

    public void RememberEmoji(string emoji)
    {
        // The dedicated storage slice owns persistence; keep the interface for parity.
    }

    private static void AddMatches(
        IReadOnlyList<AutocompleteEmojiViewModel> database,
        string q,
        List<AutocompleteEmojiViewModel> matched,
        bool allowAliases)
    {
        foreach (AutocompleteEmojiViewModel emoji in database)
        {
            if (matched.Count >= MaxEmojiSuggestions)
            {
                break;
            }

            if (emoji.Name.StartsWith(q, StringComparison.Ordinal) &&
                (allowAliases || emoji.AliasOf is null) &&
                !matched.Any(item => string.Equals(item.Emoji, emoji.Emoji, StringComparison.Ordinal)))
            {
                matched.Add(emoji);
            }
        }
    }

    private static List<AutocompleteEmojiViewModel> BuildEmojiDatabase(IEmojiCatalog catalog)
    {
        var database = new List<AutocompleteEmojiViewModel>(catalog.Emojis.Count + 32);
        foreach (UnicodeEmojiDefinition emoji in catalog.Emojis)
        {
            database.Add(new AutocompleteEmojiViewModel(emoji.Value, emoji.Name, null, null, false));
            foreach (string keyword in emoji.Keywords)
            {
                database.Add(new AutocompleteEmojiViewModel(emoji.Value, keyword, emoji.Name, null, false));
            }
        }

        return database.OrderBy(emoji => emoji.Name.Length).ToList();
    }
}
