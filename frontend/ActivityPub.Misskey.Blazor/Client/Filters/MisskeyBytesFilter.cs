using System.Globalization;

namespace ActivityPub.Misskey.Blazor.Client.Filters;

/// <summary>Misskey v12's bytes filter, kept as a pure presentation helper.</summary>
public static class MisskeyBytesFilter
{
    private static readonly string[] Sizes = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(double? value, int digits = 0)
    {
        if (value is null)
        {
            return "?";
        }

        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || digits is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value.Value == 0)
        {
            return "0";
        }

        bool negative = value.Value < 0;
        double magnitude = Math.Abs(value.Value);
        int index = Math.Min((int)Math.Floor(Math.Log(magnitude, 1024)), Sizes.Length - 1);
        double scaled = (negative ? -magnitude : magnitude) / Math.Pow(1024, index);
        string number = scaled.ToString($"F{digits}", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return number + Sizes[index];
    }
}
