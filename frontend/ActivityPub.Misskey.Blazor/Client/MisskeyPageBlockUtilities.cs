using System.Collections.ObjectModel;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyPageBlock(
    string Id,
    string Type,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<MisskeyPageBlock>? Children = null);

public sealed class MisskeyPageState
{
    private readonly Dictionary<string, object?> values;

    public MisskeyPageState(IReadOnlyDictionary<string, object?>? initial = null)
    {
        values = initial is null
            ? new(StringComparer.Ordinal)
            : new(initial, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, object?> Values => new ReadOnlyDictionary<string, object?>(values);

    public object? Get(string name) => values.TryGetValue(name, out object? value) ? value : null;

    public void Set(string name, object? value)
    {
        ValidateName(name);
        values[name] = value;
    }

    public T Get<T>(string name, T fallback = default!) =>
        Get(name) is T value ? value : fallback;

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Page variable name is invalid.", nameof(name));
        }
    }
}

public static class MisskeyPageBlockUtilities
{
    public static readonly IReadOnlySet<string> SupportedDisplayBlocks = new HashSet<string>(StringComparer.Ordinal)
    {
        "text", "section", "image", "button", "numberInput", "textInput", "textareaInput", "switch", "if", "textarea", "post", "counter", "radioButton", "canvas", "note"
    };

    public static string Interpolate(string? template, MisskeyPageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string value = template ?? string.Empty;
        if (value.Length == 0) return value;
        return System.Text.RegularExpressions.Regex.Replace(value, "\\{\\{([A-Za-z0-9_]{1,128})\\}\\}", match =>
        {
            object? result = state.Get(match.Groups[1].Value);
            return result switch
            {
                null => string.Empty,
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                _ => result.ToString() ?? string.Empty
            };
        });
    }

    public static int HeadingLevel(int parentLevel) => Math.Clamp(parentLevel, 1, 5) + 1;

    public static bool IsVisible(MisskeyPageBlock block, MisskeyPageState state)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(state);
        return !string.Equals(block.Type, "if", StringComparison.Ordinal) ||
               (block.Values.TryGetValue("var", out object? variable) && state.Get<bool>(variable?.ToString() ?? string.Empty));
    }

    public static bool TryReadImage(
        MisskeyPageBlock block,
        Uri publicBaseUri,
        out Uri? imageUri,
        out string alt)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(publicBaseUri);
        imageUri = null;
        alt = block.Values.TryGetValue("comment", out object? comment) ? comment?.ToString() ?? string.Empty : string.Empty;
        if (!block.Values.TryGetValue("url", out object? url) || url is not string text || !Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri? parsed))
        {
            return false;
        }

        Uri resolved = parsed.IsAbsoluteUri ? parsed : new Uri(publicBaseUri, text);
        if (resolved.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(resolved.UserInfo))
        {
            return false;
        }

        imageUri = resolved;
        return true;
    }

    public static (int Width, int Height) CanvasSize(MisskeyPageBlock block)
    {
        int width = ReadPositiveInt(block, "width", 300);
        int height = ReadPositiveInt(block, "height", 150);
        return (Math.Min(width, 4096), Math.Min(height, 4096));
    }

    public static string ButtonClass(bool primary) => primary ? "kudkigyw primary" : "kudkigyw";

    private static int ReadPositiveInt(MisskeyPageBlock block, string key, int fallback) =>
        block.Values.TryGetValue(key, out object? value) && value is IConvertible convertible &&
        int.TryParse(convertible.ToString(System.Globalization.CultureInfo.InvariantCulture), out int result) && result > 0
            ? result
            : fallback;
}
