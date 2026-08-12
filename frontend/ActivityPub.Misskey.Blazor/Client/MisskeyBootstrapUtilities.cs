namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// Deterministic parts of the v12 bootstrap sequence.  Network calls, OIDC
/// callback handling, and rendering remain owned by the host; this type keeps
/// language and shell selection rules testable without a Vue runtime.
/// </summary>
public enum MisskeyShellKind
{
    Visitor,
    Universal,
    Classic,
    Deck,
    Zen,
}

public static class MisskeyBootstrapUtilities
{
    public static string ChooseLanguage(
        IReadOnlyList<string> supported,
        string? saved,
        string? browserLanguage)
    {
        ArgumentNullException.ThrowIfNull(supported);
        string browser = browserLanguage?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(saved) && supported.Contains(saved, StringComparer.Ordinal))
        {
            return saved;
        }

        if (supported.Contains(browser, StringComparer.Ordinal))
        {
            return browser;
        }

        string primary = browser.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        if (primary.Length > 0)
        {
            string? match = supported.FirstOrDefault(language =>
                language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0]
                    .Equals(primary, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        string? english = supported.FirstOrDefault(language => language.Equals("en-US", StringComparison.Ordinal));
        return english
            ?? (supported.Count > 0 ? supported[0] : null)
            ?? throw new ArgumentException("At least one supported locale is required.", nameof(supported));
    }

    public static MisskeyShellKind SelectShell(
        bool authenticated,
        string? configuredUi,
        bool zenQuery,
        bool classicEnabled = true,
        bool deckEnabled = true)
    {
        if (zenQuery)
        {
            return MisskeyShellKind.Zen;
        }

        if (!authenticated)
        {
            return MisskeyShellKind.Visitor;
        }

        if (configuredUi?.Equals("deck", StringComparison.OrdinalIgnoreCase) == true && deckEnabled)
        {
            return MisskeyShellKind.Deck;
        }

        if (configuredUi?.Equals("classic", StringComparison.OrdinalIgnoreCase) == true && classicEnabled)
        {
            return MisskeyShellKind.Classic;
        }

        return MisskeyShellKind.Universal;
    }

    public static string SafeInitializationErrorCode(Exception exception) => exception switch
    {
        OperationCanceledException => "CLIENT_INIT_CANCELLED",
        UnauthorizedAccessException => "CLIENT_INIT_AUTHORITY_REJECTED",
        InvalidDataException => "CLIENT_INIT_INVALID_CONFIGURATION",
        HttpRequestException => "CLIENT_INIT_NETWORK_FAILURE",
        _ => "CLIENT_INIT_FAILED",
    };
}
