namespace ActivityPub.Misskey.Blazor.Security;

public static class SameOriginMediaUrl
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value[0] != '/' ||
            (value.Length > 1 && value[1] == '/') ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            return null;
        }

        return value;
    }

    public static string? CssBackgroundImage(string? value)
    {
        string? normalized = Normalize(value);
        return normalized is null
            ? null
            : $"background-image: url('{normalized.Replace("'", "%27", StringComparison.Ordinal)}')";
    }
}
