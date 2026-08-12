using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ActivityPub.Misskey.Blazor.State;

internal static partial class MisskeyClientVersionComparer
{
    // This is the compare-versions 5.0.1 grammar imported by Misskey 12.119.2 init.ts.
    [GeneratedRegex(
        @"^[v^~<>=]*?(\d+)(?:\.([x*]|\d+)(?:\.([x*]|\d+)(?:\.([x*]|\d+))?(?:-([\da-z\-]+(?:\.[\da-z\-]+)*))?(?:\+[\da-z\-]+(?:\.[\da-z\-]+)*)?)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^-?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingIntegerPattern();

    public static bool TryCompare(string first, string second, out int comparison)
    {
        comparison = 0;
        if (!TryParse(first, out ParsedVersion left) || !TryParse(second, out ParsedVersion right))
        {
            return false;
        }

        comparison = CompareSegments(left.NumericSegments, right.NumericSegments);
        if (comparison != 0)
        {
            return true;
        }

        if (left.PreRelease is not null && right.PreRelease is not null)
        {
            comparison = CompareSegments(left.PreRelease.Split('.'), right.PreRelease.Split('.'));
        }
        else if (left.PreRelease is not null || right.PreRelease is not null)
        {
            comparison = left.PreRelease is not null ? -1 : 1;
        }

        return true;
    }

    private static bool TryParse(string version, out ParsedVersion parsed)
    {
        parsed = null!;
        if (version is null)
        {
            return false;
        }

        Match match = VersionPattern().Match(version);
        if (!match.Success)
        {
            return false;
        }

        parsed = new ParsedVersion(
            [match.Groups[1].Value, ValueOrNull(match.Groups[2]), ValueOrNull(match.Groups[3]), ValueOrNull(match.Groups[4])],
            ValueOrNull(match.Groups[5]));
        return true;
    }

    private static int CompareSegments(IReadOnlyList<string?> left, IReadOnlyList<string?> right)
    {
        int length = Math.Max(left.Count, right.Count);
        for (int index = 0; index < length; index++)
        {
            string leftValue = index < left.Count ? left[index] ?? "0" : "0";
            string rightValue = index < right.Count ? right[index] ?? "0" : "0";
            int comparison = CompareStrings(leftValue, rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CompareStrings(string left, string right)
    {
        if (IsWildcard(left) || IsWildcard(right))
        {
            return 0;
        }

        bool leftNumeric = TryParseJavaScriptInteger(left, out BigInteger leftNumber);
        bool rightNumeric = TryParseJavaScriptInteger(right, out BigInteger rightNumber);
        if (leftNumeric && rightNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        string comparableLeft = leftNumeric ? leftNumber.ToString(CultureInfo.InvariantCulture) : left;
        string comparableRight = rightNumeric ? rightNumber.ToString(CultureInfo.InvariantCulture) : right;
        return Math.Sign(string.CompareOrdinal(comparableLeft, comparableRight));
    }

    private static bool TryParseJavaScriptInteger(string value, out BigInteger number)
    {
        number = default;
        Match match = LeadingIntegerPattern().Match(value);
        return match.Success && BigInteger.TryParse(
            match.Value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool IsWildcard(string value) => value is "*" or "x" or "X";

    private static string? ValueOrNull(Group group) => group.Success ? group.Value : null;

    private sealed record ParsedVersion(IReadOnlyList<string?> NumericSegments, string? PreRelease);
}
