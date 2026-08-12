using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ActivityPub.Misskey.Blazor.Localization;

public sealed class MisskeyLocaleRequestResolver(IMisskeyLocaleCatalog catalog)
{
    public const string CookieName = "misskey.lang";
    public const string DefaultLocale = "ja-JP";

    public string Resolve(HttpContext? context)
    {
        if (context is null)
        {
            return DefaultLocale;
        }

        if (catalog.TryCanonicalize(context.Request.Cookies[CookieName], out string cookieLocale))
        {
            return cookieLocale;
        }

        return ResolveAcceptLanguage(context.Request.Headers.AcceptLanguage);
    }

    internal string ResolveAcceptLanguage(StringValues headerValues)
    {
        var candidates = new List<LanguageCandidate>();
        int order = 0;
        foreach (string? header in headerValues)
        {
            foreach (string segment in (header ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (order >= 32)
                {
                    break;
                }

                string[] parts = segment.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string range = parts[0].Trim();
                double quality = 1;
                string? qualityPart = parts.Skip(1).FirstOrDefault(part => part.StartsWith("q=", StringComparison.OrdinalIgnoreCase));
                if (qualityPart is not null &&
                    (!double.TryParse(qualityPart.AsSpan(2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quality) ||
                     quality is < 0 or > 1))
                {
                    quality = 0;
                }

                candidates.Add(new LanguageCandidate(range, quality, order++));
            }
        }

        foreach (LanguageCandidate candidate in candidates
                     .Where(candidate => candidate.Quality > 0)
                     .OrderByDescending(candidate => candidate.Quality)
                     .ThenBy(candidate => candidate.Order))
        {
            if (catalog.TryCanonicalize(candidate.Range, out string exact))
            {
                return exact;
            }

            if (candidate.Range.Length is > 0 and <= 35 && candidate.Range != "*" && !candidate.Range.Any(char.IsControl))
            {
                string primaryLanguage = candidate.Range.Split('-', 2)[0];
                MisskeyLocaleDefinition? primary = catalog.Locales.FirstOrDefault(definition =>
                    definition.Locale.StartsWith($"{primaryLanguage}-", StringComparison.OrdinalIgnoreCase));
                if (primary is not null)
                {
                    return primary.Locale;
                }
            }
        }

        return DefaultLocale;
    }

    private sealed record LanguageCandidate(string Range, double Quality, int Order);
}
