using System.Globalization;

namespace ActivityPub.Misskey.Blazor.Client;

public enum MisskeySearchIntentKind
{
    Empty,
    Account,
    Tag,
    Date,
    RemoteIri,
    Text,
}

public sealed record MisskeySearchIntent(
    MisskeySearchIntentKind Kind,
    string Query,
    string? Account = null,
    string? Tag = null,
    DateTimeOffset? Date = null,
    string? RemoteIri = null);

public static class MisskeySearchUtilities
{
    public static async ValueTask<string?> LookupUserAsync(
        string input,
        Func<MisskeyUserLookup, CancellationToken, ValueTask<string?>> resolve,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(resolve);

        MisskeyUserLookup parsed = ParseAccount(input);
        string? result = await resolve(parsed, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            return result;
        }

        return await resolve(new MisskeyUserLookup(null, input), cancellationToken).ConfigureAwait(false);
    }

    public static MisskeySearchIntent Parse(string? value)
    {
        string query = value?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return new(MisskeySearchIntentKind.Empty, string.Empty);
        }

        if (query.StartsWith('@') && !query.Contains(' ', StringComparison.Ordinal))
        {
            return new(MisskeySearchIntentKind.Account, query, Account: query);
        }

        if (query.StartsWith('#'))
        {
            return new(MisskeySearchIntentKind.Tag, query, Tag: query[1..]);
        }

        string normalized = query.Replace('-', '/');
        if (DateTime.TryParseExact(
                normalized,
                ["yyyy/MM/dd", "yyyy/MM/dd HH:mm", "yyyy/MM/dd HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime date))
        {
            if (normalized.Length == 10)
            {
                date = date.Date.AddDays(1).AddMilliseconds(-1);
            }

            return new(MisskeySearchIntentKind.Date, query, Date: new DateTimeOffset(date));
        }

        if (query.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(query, UriKind.Absolute, out Uri? remote))
        {
            return new(MisskeySearchIntentKind.RemoteIri, query, RemoteIri: remote.AbsoluteUri);
        }

        return new(MisskeySearchIntentKind.Text, query);
    }

    private static MisskeyUserLookup ParseAccount(string input)
    {
        string value = input[0] == '@' ? input[1..] : input;
        int at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1
            ? new(value[..at], value[(at + 1)..])
            : new(value, null);
    }
}

public sealed record MisskeyUserLookup(string? Username, string? HostOrUserId);
