using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ActivityPub.Misskey.Blazor.Client;

public static class MisskeyArrayUtilities
{
    public static int CountIf<T>(IEnumerable<T> values, Func<T, bool> predicate) => values.Count(predicate);

    public static int Count<T>(T value, IEnumerable<T> values) => values.Count(item => EqualityComparer<T>.Default.Equals(item, value));

    public static IReadOnlyList<T> Concat<T>(IEnumerable<IEnumerable<T>> values) => values.SelectMany(static value => value).ToArray();

    public static IReadOnlyList<T> Intersperse<T>(T separator, IReadOnlyList<T> values)
    {
        List<T> result = [];
        for (int index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                result.Add(separator);
            }
            result.Add(values[index]);
        }
        return result;
    }

    public static IReadOnlyList<T> Erase<T>(T value, IEnumerable<T> values) =>
        values.Where(item => !EqualityComparer<T>.Default.Equals(item, value)).ToArray();

    public static IReadOnlyList<T> Difference<T>(IEnumerable<T> values, IEnumerable<T> other) =>
        values.Except(other).ToArray();

    public static IReadOnlyList<T> Unique<T>(IEnumerable<T> values) => values.Distinct().ToArray();

    public static IReadOnlyList<T> UniqueBy<T, TKey>(IEnumerable<T> values, Func<T, TKey> keySelector)
    {
        HashSet<TKey> seen = [];
        return values.Where(value => seen.Add(keySelector(value))).ToArray();
    }

    public static double Sum(IEnumerable<double> values) => values.Sum();

    public static double Maximum(IEnumerable<double> values) => values.Any() ? values.Max() : double.NegativeInfinity;

    public static IReadOnlyList<IReadOnlyList<T>> GroupBy<T>(
        IEnumerable<T> values,
        Func<T, T, bool> equivalent)
    {
        List<IReadOnlyList<T>> result = [];
        foreach (T value in values)
        {
            if (result.Count > 0 && equivalent(result[^1][0], value))
            {
                result[^1] = result[^1].Append(value).ToArray();
            }
            else
            {
                result.Add([value]);
            }
        }
        return result;
    }

    public static IReadOnlyList<IReadOnlyList<T>> GroupOn<T, TKey>(IEnumerable<T> values, Func<T, TKey> keySelector) =>
        GroupBy(values, (left, right) => EqualityComparer<TKey>.Default.Equals(keySelector(left), keySelector(right)));

    public static IReadOnlyDictionary<string, IReadOnlyList<T>> GroupByKey<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector)
    {
        Dictionary<string, List<T>> grouped = new(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string key = keySelector(value);
            if (!grouped.TryGetValue(key, out List<T>? bucket))
            {
                bucket = [];
                grouped.Add(key, bucket);
            }
            bucket.Add(value);
        }
        return grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<T>)pair.Value, StringComparer.Ordinal);
    }

    public static bool LessThan(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        for (int index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            if (left[index] < right[index]) return true;
            if (left[index] > right[index]) return false;
        }
        return left.Count < right.Count;
    }

    public static IReadOnlyList<T> TakeWhile<T>(IEnumerable<T> values, Func<T, bool> predicate)
    {
        List<T> result = [];
        foreach (T value in values)
        {
            if (!predicate(value)) break;
            result.Add(value);
        }
        return result;
    }

    public static IReadOnlyList<double> CumulativeSum(IEnumerable<double> values)
    {
        List<double> result = [];
        double total = 0;
        foreach (double value in values)
        {
            total += value;
            result.Add(total);
        }
        return result;
    }

    public static IReadOnlyList<T> ToArray<T>(T? value) => value is null ? [] : [value];

    public static T? ToSingle<T>(IReadOnlyList<T>? values) => values is { Count: > 0 } ? values[0] : default;
}

public static partial class MisskeyScriptUtilities
{
    [GeneratedRegex(@"\{\{(\w+)(:(\w+))?\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex LocaleTokenRegex();

    [GeneratedRegex(@"\[[^\[]*\]|(?:yyyy|yy|MMMM|MMM|MM|M|dd|d|HH|H|hh|h|mm|m|ss|s|tt)", RegexOptions.CultureInvariant)]
    private static partial Regex TimeTokenRegex();

    public static string GetUserName(string? name, string username) =>
        string.IsNullOrEmpty(name) ? username : name;

    public static string SafeUriDecode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    public static string FormatTimeString(DateTime date, string format, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        culture ??= CultureInfo.CurrentCulture;
        return TimeTokenRegex().Replace(format, match =>
        {
            string token = match.Value;
            if (token.Length > 0 && token[0] == '[')
            {
                return LocaleTokenRegex().Replace(token[1..^1], localeMatch =>
                {
                    string kind = localeMatch.Groups[1].Value;
                    string? option = localeMatch.Groups[3].Success ? localeMatch.Groups[3].Value : null;
                    return kind switch
                    {
                        "weekday" => date.ToString(option == "long" ? "dddd" : "ddd", culture),
                        "era" => date.ToString("gg", culture),
                        "year" => date.ToString("yyyy", culture),
                        "month" => date.ToString(option == "long" ? "MMMM" : "M", culture),
                        "day" => date.ToString("d", culture),
                        "hour" => date.ToString("h", culture),
                        "minute" => date.ToString("m", culture),
                        "second" => date.ToString("s", culture),
                        "timeZoneName" => date.ToString("zzz", culture),
                        _ => localeMatch.Value
                    };
                });
            }
            return token switch
            {
                "yyyy" => date.ToString("yyyy", culture),
                "yy" => date.ToString("yy", culture),
                "MMMM" => date.ToString("MMMM", culture),
                "MMM" => date.ToString("MMM", culture),
                "MM" => date.ToString("MM", culture),
                "M" => date.ToString("M", culture),
                "dd" => date.ToString("dd", culture),
                "d" => date.ToString("d", culture),
                "HH" => date.ToString("HH", CultureInfo.InvariantCulture),
                "H" => date.ToString("H", CultureInfo.InvariantCulture),
                "hh" => date.ToString("hh", CultureInfo.InvariantCulture),
                "h" => date.ToString("h", CultureInfo.InvariantCulture),
                "mm" => date.ToString("mm", CultureInfo.InvariantCulture),
                "m" => date.ToString("m", CultureInfo.InvariantCulture),
                "ss" => date.ToString("ss", CultureInfo.InvariantCulture),
                "s" => date.ToString("s", CultureInfo.InvariantCulture),
                "tt" => date.ToString("tt", CultureInfo.InvariantCulture),
                _ => token
            };
        });
    }

    public static string GetStaticImageUrl(Uri instanceUri, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(instanceUri);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? original) ||
            !string.Equals(original.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(original.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(original.UserInfo))
        {
            throw new ArgumentException("Image URL must be an absolute HTTP(S) URL without userinfo.", nameof(baseUrl));
        }

        string instance = instanceUri.AbsoluteUri.TrimEnd('/');
        if (original.AbsoluteUri.StartsWith($"{instance}/proxy/", StringComparison.Ordinal))
        {
            return AddQuery(original, "static", "1");
        }

        string dummy = original.Host + original.AbsolutePath;
        return $"{instance}/proxy/{dummy}?url={Uri.EscapeDataString(original.AbsoluteUri)}&static=1";
    }

    private static string AddQuery(Uri value, string key, string queryValue)
    {
        string separator = string.IsNullOrEmpty(value.Query) ? "?" : "&";
        return value.AbsoluteUri + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(queryValue);
    }
}

public sealed record MisskeyNoteSummaryInput(
    string? Text = null,
    string? ContentWarning = null,
    int FileCount = 0,
    bool HasPoll = false,
    bool HasReply = false,
    MisskeyNoteSummaryInput? Reply = null,
    bool HasRenote = false,
    MisskeyNoteSummaryInput? Renote = null,
    bool IsDeleted = false,
    bool IsHidden = false);

public static class MisskeyNoteSummary
{
    public static string Format(
        MisskeyNoteSummaryInput note,
        string deletedLabel = "deletedNote",
        string invisibleLabel = "invisibleNote",
        string pollLabel = "poll",
        Func<int, string>? filesLabel = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        filesLabel ??= static count => $"withNFiles:{count}";
        if (note.IsDeleted) return $"({deletedLabel})";
        if (note.IsHidden) return $"({invisibleLabel})";

        string summary = note.ContentWarning ?? note.Text ?? string.Empty;
        if (note.FileCount > 0) summary += $" ({filesLabel(note.FileCount)})";
        if (note.HasPoll) summary += $" ({pollLabel})";
        if (note.HasReply) summary += $"\n\nRE: {(note.Reply is null ? "..." : Format(note.Reply, deletedLabel, invisibleLabel, pollLabel, filesLabel))}";
        if (note.HasRenote) summary += $"\n\nRN: {(note.Renote is null ? "..." : Format(note.Renote, deletedLabel, invisibleLabel, pollLabel, filesLabel))}";
        return summary.Trim();
    }
}

public static class MisskeyWordMute
{
    public static bool Matches(
        string? noteUserId,
        string? viewerId,
        string? contentWarning,
        string? text,
        IEnumerable<string> mutedWords)
    {
        if (!string.IsNullOrEmpty(viewerId) && string.Equals(noteUserId, viewerId, StringComparison.Ordinal))
        {
            return false;
        }

        string value = $"{contentWarning ?? string.Empty}\n{text ?? string.Empty}".Trim();
        if (value.Length == 0)
        {
            return false;
        }

        foreach (string filter in mutedWords)
        {
            if (filter.Length > 0 && filter[0] == '/' && filter.LastIndexOf('/') > 0)
            {
                int end = filter.LastIndexOf('/');
                string pattern = filter[1..end];
                string options = filter[(end + 1)..];
                RegexOptions regexOptions = options.Contains('i')
                    ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                    : RegexOptions.CultureInvariant;
                try
                {
                    if (Regex.IsMatch(value, pattern, regexOptions)) return true;
                }
                catch (ArgumentException)
                {
                    // The upstream input boundary discards invalid patterns.
                }
                continue;
            }

            string[] keywords = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (keywords.Length > 0 && keywords.All(value.Contains))
            {
                return true;
            }
        }
        return false;
    }
}

public static class MisskeyShuffle
{
    public static void Shuffle<T>(IList<T> values, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        random ??= Random.Shared;
        for (int index = values.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}

public static class MisskeyKeyCodes
{
    private static readonly Dictionary<string, string[]> Aliases = BuildAliases();

    public static IReadOnlyList<string> Resolve(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Aliases.TryGetValue(input.ToLowerInvariant(), out string[]? values) ? values : [input];
    }

    private static Dictionary<string, string[]> BuildAliases()
    {
        Dictionary<string, string[]> values = new(StringComparer.Ordinal)
        {
            ["esc"] = ["Escape"],
            ["enter"] = ["Enter", "NumpadEnter"],
            ["up"] = ["ArrowUp"],
            ["down"] = ["ArrowDown"],
            ["left"] = ["ArrowLeft"],
            ["right"] = ["ArrowRight"],
            ["plus"] = ["NumpadAdd", "Semicolon"]
        };
        for (char value = 'a'; value <= 'z'; value++) values[value.ToString()] = [$"Key{char.ToUpperInvariant(value)}"];
        for (int value = 0; value <= 9; value++) values[value.ToString(CultureInfo.InvariantCulture)] = [$"Numpad{value}", $"Digit{value}"];
        return values;
    }
}

public static class MisskeyTime
{
    public static DateTime DateUtc(IReadOnlyList<int> values)
    {
        if (values.Count is < 2 or > 7) throw new ArgumentException("Expected two to seven UTC date values.", nameof(values));
        int day = values.Count > 2 ? values[2] : 1;
        int hour = values.Count > 3 ? values[3] : 0;
        int minute = values.Count > 4 ? values[4] : 0;
        int second = values.Count > 5 ? values[5] : 0;
        int millisecond = values.Count > 6 ? values[6] : 0;
        return new DateTime(values[0], values[1] + 1, day, hour, minute, second, millisecond, DateTimeKind.Utc);
    }

    public static bool IsSame(DateTime left, DateTime right) => left == right;
    public static bool IsBefore(DateTime left, DateTime right) => left < right;
    public static bool IsAfter(DateTime left, DateTime right) => left > right;

    public static DateTime Add(DateTime value, double amount, string span = "ms") =>
        value.AddMilliseconds(amount * span switch
        {
            "day" => 86_400_000,
            "hour" => 3_600_000,
            "ms" => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(span))
        });

    public static DateTime Subtract(DateTime value, double amount, string span = "ms") => Add(value, -amount, span);
}

public static class MisskeyUrl
{
    public static string Query(IReadOnlyDictionary<string, object?> values)
    {
        List<string> parts = [];
        foreach ((string key, object? value) in values)
        {
            if (value is null) continue;
            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                List<object?> items = [];
                foreach (object? item in enumerable) items.Add(item);
                if (items.Count == 0) continue;
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(string.Join(',', items.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))))}");
            }
            else
            {
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}");
            }
        }
        return string.Join('&', parts);
    }

    public static string AppendQuery(string url, string query) =>
        url + (url.Contains('?') ? (url.EndsWith('?') ? string.Empty : "&") : "?") + query;
}

public static class MisskeyLoginId
{
    public static string Add(string url, string loginId, Uri baseUri)
    {
        Uri value = new(baseUri, url);
        return AddOrReplace(value, loginId).AbsoluteUri;
    }

    public static string Remove(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? value)) throw new ArgumentException("The URL must be absolute.", nameof(url));
        Dictionary<string, string> query = ParseQuery(value.Query);
        query.Remove("loginId");
        UriBuilder builder = new(value) { Query = BuildQuery(query) };
        return builder.Uri.AbsoluteUri;
    }

    private static Uri AddOrReplace(Uri value, string loginId)
    {
        Dictionary<string, string> query = ParseQuery(value.Query);
        query["loginId"] = loginId;
        UriBuilder builder = new(value) { Query = BuildQuery(query) };
        return builder.Uri;
    }

    private static Dictionary<string, string> ParseQuery(string value)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string part in value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            result[Uri.UnescapeDataString(pair[0])] = pair.Length == 1 ? string.Empty : Uri.UnescapeDataString(pair[1]);
        }
        return result;
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> values) =>
        string.Join('&', values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
}

public static class MisskeyTwemoji
{
    public const string SvgBase = "/twemoji";

    public static string CharToFileName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool hasJoiner = value.Contains('\u200d');
        IEnumerable<string> codePoints = value.EnumerateRunes()
            .Select(rune => rune.Value.ToString("x", CultureInfo.InvariantCulture))
            .Where(code => hasJoiner || !string.Equals(code, "fe0f", StringComparison.Ordinal))
            .Where(code => code.Length > 0);
        return string.Join('-', codePoints);
    }

    public static string CharToFilePath(string value) => $"{SvgBase}/{CharToFileName(value)}.svg";
}
