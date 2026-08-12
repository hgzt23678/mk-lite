using System.Text;
using System.Text.RegularExpressions;
using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed class UrlPreviewService(
    IUrlPreviewRepository repository,
    IUrlPreviewFetcher fetcher,
    IClock clock) : IUrlPreviewService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    public async Task<UrlPreviewResult?> GetAsync(string url, string? lang, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        string? canonical = Canonicalize(url);
        if (canonical is null)
        {
            return null;
        }

        UrlPreview? cached = await repository.FindByUrlAsync(canonical, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        if (cached is not null && !cached.IsExpired(now))
        {
            return ToResult(cached);
        }

        UrlPreviewResult? fetched = await fetcher.FetchAsync(canonical, lang, cancellationToken).ConfigureAwait(false);
        if (fetched is null)
        {
            return cached is null ? null : ToResult(cached);
        }

        UrlPreview stored = UrlPreview.Create(
            canonical,
            fetched.Title,
            fetched.Description,
            fetched.Thumbnail,
            fetched.Icon,
            fetched.SiteName,
            fetched.PlayerUrl,
            fetched.PlayerWidth,
            fetched.PlayerHeight,
            now,
            CacheLifetime);
        await repository.SaveAsync(stored, cancellationToken).ConfigureAwait(false);
        return fetched;
    }

    private static UrlPreviewResult ToResult(UrlPreview preview) =>
        new(
            preview.Title,
            preview.Description,
            preview.Thumbnail,
            preview.Icon,
            preview.SiteName,
            preview.PlayerUrl,
            preview.PlayerWidth,
            preview.PlayerHeight);

    private static string? Canonicalize(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        uri = new Uri(uri.GetLeftPart(UriPartial.Path));
        return uri.AbsoluteUri.TrimEnd('/');
    }
}

public interface IUrlPreviewFetcher
{
    Task<UrlPreviewResult?> FetchAsync(string url, string? lang, CancellationToken cancellationToken);
}

public sealed class HtmlMetaParser
{
    private static readonly Regex MetaPattern = new(
        @"<meta\s+[^>]*(?:property|name)\s*=\s*[""']([^""']+)[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TitlePattern = new(
        @"<title[^>]*>([^<]*)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IconPattern = new(
        @"<link[^>]*rel\s*=\s*[""'][^""']*icon[^""']*[""'][^>]*href\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static HtmlMetaSummary Parse(string html, string baseUrl)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MetaPattern.Matches(html))
        {
            string key = match.Groups[1].Value.Trim();
            string content = Decode(match.Groups[2].Value).Trim();
            if (key.Length > 0 && content.Length > 0 && !values.ContainsKey(key))
            {
                values[key] = content;
            }
        }

        string title = First(values, "og:title", "twitter:title");
        if (title.Length == 0)
        {
            Match titleMatch = TitlePattern.Match(html);
            if (titleMatch.Success)
            {
                title = Decode(titleMatch.Groups[1].Value).Trim();
            }
        }

        string? siteName = Optional(values, "og:site_name", "twitter:site");
        string? description = Optional(values, "og:description", "twitter:description", "description");
        string? thumbnail = Optional(values, "og:image", "twitter:image");
        string? icon = OptionalIcon(html, baseUrl);
        string? playerUrl = Optional(values, "og:video:url", "og:video", "twitter:player");
        int? playerWidth = OptionalInt(values, "og:video:width", "twitter:player:width");
        int? playerHeight = OptionalInt(values, "og:video:height", "twitter:player:height");
        return new HtmlMetaSummary(title, description, thumbnail, icon, siteName, playerUrl, playerWidth, playerHeight);
    }

    private static string? OptionalIcon(string html, string baseUrl)
    {
        Match match = IconPattern.Match(html);
        if (!match.Success)
        {
            return null;
        }

        string value = Decode(match.Groups[1].Value).Trim();
        return Resolve(value, baseUrl);
    }

    private static string First(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out string? value) && value.Length > 0)
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        string value = First(values, keys);
        return value.Length > 0 ? value : null;
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        string value = First(values, keys);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
    }

    private static string Decode(string value)
    {
        string decoded = System.Net.WebUtility.HtmlDecode(value);
        decoded = decoded.Replace("&amp;", "&", StringComparison.Ordinal);
        return decoded;
    }

    private static string? Resolve(string value, string baseUrl)
    {
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute.AbsoluteUri;
        }

        if (Uri.TryCreate(new Uri(baseUrl), value, out Uri? relative) &&
            relative.Scheme is "http" or "https")
        {
            return relative.AbsoluteUri;
        }

        return null;
    }
}

public sealed record HtmlMetaSummary(
    string Title,
    string? Description,
    string? Thumbnail,
    string? Icon,
    string? SiteName,
    string? PlayerUrl,
    int? PlayerWidth,
    int? PlayerHeight);
