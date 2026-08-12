using System.Globalization;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Client.Filters;

public static class MisskeyUserFilter
{
    public static string Acct(NoteAuthorViewModel user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Acct;
    }

    public static string UserName(NoteAuthorViewModel user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
    }

    public static string UserPage(NoteAuthorViewModel user, string? path = null, Uri? publicBaseUri = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        string suffix = string.IsNullOrWhiteSpace(path) ? string.Empty : "/" + SanitizePath(path);
        string relative = "/@" + NormalizeAcct(user.Acct) + suffix;
        return publicBaseUri is null ? relative : new Uri(publicBaseUri, relative).AbsoluteUri;
    }

    private static string NormalizeAcct(string value)
    {
        int separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return Uri.EscapeDataString(value);
        }

        string username = Uri.EscapeDataString(value[..separator]);
        string host = value[(separator + 1)..];
        string asciiHost;
        try
        {
            asciiHost = new IdnMapping().GetAscii(host);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("The user account host is invalid.", nameof(value));
        }

        return username + "@" + asciiHost.ToLowerInvariant();
    }

    private static string SanitizePath(string path)
    {
        string value = path.Trim().Trim('/');
        if (value.Length == 0 || value.Length > 256 || value.Any(char.IsControl) || value.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("The user page path is invalid.", nameof(path));
        }

        return value;
    }
}
