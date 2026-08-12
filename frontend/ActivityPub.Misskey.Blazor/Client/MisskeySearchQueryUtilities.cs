namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// Port of Misskey v12's <c>genSearchQuery</c> normalization.  User lookup is
/// injected so this utility cannot bypass the typed API client or viewer
/// authorization boundary.
/// </summary>
public static class MisskeySearchQueryUtilities
{
    public static async ValueTask<MisskeySearchQuery> GenerateAsync(
        string? value,
        string query,
        string localHost,
        Func<string, CancellationToken, ValueTask<string?>>? resolveUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(localHost);

        string? host = null;
        string? userId = null;
        string[] tokens = query.Split(' ');
        foreach (string token in tokens.Where(token => token.Length > 0 && token[0] == '@'))
        {
            string account = token[1..];
            if (account.Contains('.', StringComparison.Ordinal))
            {
                // Keep the upstream value exactly as supplied.  The Misskey
                // endpoint accepts this string as its host filter.
                host = string.Equals(account, localHost, StringComparison.OrdinalIgnoreCase) || account == "."
                    ? null
                    : account;
            }
            else if (resolveUserId is not null)
            {
                userId = await resolveUserId(account, cancellationToken).ConfigureAwait(false);
            }
        }

        return new(
            Query: string.Join(' ', tokens.Where(token =>
                !(token.Length > 0 && token[0] == '/') &&
                !(token.Length > 0 && token[0] == '@'))),
            Host: host,
            UserId: userId);
    }
}

public sealed record MisskeySearchQuery(string Query, string? Host, string? UserId);
