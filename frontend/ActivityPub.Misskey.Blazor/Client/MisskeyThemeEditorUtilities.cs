namespace ActivityPub.Misskey.Blazor.Client;

public enum MisskeyThemeValueKind
{
    Color,
    Function,
    PropertyReference,
    ConstantReference,
    Css,
}

public sealed record MisskeyThemeValue(
    MisskeyThemeValueKind Kind,
    string? Value = null,
    string? FunctionName = null,
    double? Argument = null);

public sealed record MisskeyThemeEditorTheme(
    string Id,
    string Name,
    string Author,
    string? Description,
    string Base,
    IReadOnlyDictionary<string, string> Properties);

public static class MisskeyThemeEditorUtilities
{
    public static MisskeyThemeValue? FromThemeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith(':'))
        {
            string[] parts = value[1..].Split('<', 3);
            if (parts.Length != 3 || !double.TryParse(parts[1], out double argument))
            {
                return null;
            }

            string functionValue = parts[2].StartsWith('@') ? parts[2][1..] : string.Empty;
            return new(MisskeyThemeValueKind.Function, functionValue, parts[0], argument);
        }

        if (value.StartsWith('@'))
        {
            return new(MisskeyThemeValueKind.PropertyReference, value[1..]);
        }

        if (value.StartsWith('$'))
        {
            return new(MisskeyThemeValueKind.ConstantReference, value[1..]);
        }

        if (value.StartsWith('"'))
        {
            return new(MisskeyThemeValueKind.Css, value[1..].Trim());
        }

        return new(MisskeyThemeValueKind.Color, value);
    }

    public static string? ToThemeString(MisskeyThemeValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Kind switch
        {
            MisskeyThemeValueKind.Color => value.Value,
            MisskeyThemeValueKind.Function when value.FunctionName is not null && value.Argument is not null
                => $":{value.FunctionName}<{value.Argument.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}<@{value.Value}",
            MisskeyThemeValueKind.PropertyReference => $"@{value.Value}",
            MisskeyThemeValueKind.ConstantReference => $"${value.Value}",
            MisskeyThemeValueKind.Css => $"\" {value.Value}",
            _ => null,
        };
    }

    public static MisskeyThemeEditorTheme ConvertToMisskeyTheme(
        IReadOnlyList<KeyValuePair<string, MisskeyThemeValue?>> values,
        string name,
        string description,
        string author,
        string baseTheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        if (baseTheme is not "light" and not "dark")
        {
            throw new ArgumentException("Theme base must be light or dark.", nameof(baseTheme));
        }

        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        foreach ((string key, MisskeyThemeValue? value) in values)
        {
            string? encoded = ToThemeString(value);
            if (encoded is not null)
            {
                properties[key] = encoded;
            }
        }

        return new(Guid.NewGuid().ToString(), name, author, description, baseTheme, properties);
    }

    public static IReadOnlyList<KeyValuePair<string, MisskeyThemeValue?>> ConvertToViewModel(
        MisskeyThemeEditorTheme theme,
        IEnumerable<string> themeProperties)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(themeProperties);

        List<KeyValuePair<string, MisskeyThemeValue?>> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string key in themeProperties)
        {
            if (key.StartsWith('X') || !seen.Add(key))
            {
                continue;
            }

            theme.Properties.TryGetValue(key, out string? value);
            result.Add(new(key, FromThemeString(value)));
        }

        foreach ((string key, string value) in theme.Properties.Where(item => item.Key.StartsWith('$')))
        {
            if (seen.Add(key))
            {
                result.Add(new(key, FromThemeString(value)));
            }
        }

        return result;
    }
}
