namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyHotkeyPattern(
    IReadOnlySet<string> Codes,
    bool Ctrl,
    bool Shift,
    bool Alt);

public sealed record MisskeyHotkeyAction(
    IReadOnlyList<MisskeyHotkeyPattern> Patterns,
    bool AllowRepeat);

public sealed record MisskeyKeyboardEvent(
    string Code,
    bool CtrlKey = false,
    bool ShiftKey = false,
    bool AltKey = false,
    bool MetaKey = false,
    bool Repeat = false,
    string? TargetTagName = null,
    bool ContentEditable = false);

/// <summary>Typed equivalent of v12's hotkey directive and keymap parser.</summary>
public static class MisskeyHotkeyUtilities
{
    public static IReadOnlyList<MisskeyHotkeyAction> Parse(
        IReadOnlyDictionary<string, Action<MisskeyKeyboardEvent>> keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        return keymap.Select(item =>
        {
            string expression = item.Key.Trim();
            bool allowRepeat = true;
            if (expression.StartsWith('(') && expression.EndsWith(')'))
            {
                allowRepeat = false;
                expression = expression[1..^1];
            }

            IReadOnlyList<MisskeyHotkeyPattern> patterns = expression
                .Split('|', StringSplitOptions.TrimEntries)
                .Select(ParsePattern)
                .ToArray();
            return new MisskeyHotkeyAction(patterns, allowRepeat);
        }).ToArray();
    }

    public static bool Matches(
        MisskeyKeyboardEvent keyboard,
        MisskeyHotkeyAction action,
        bool ignoreFormControls = true)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(action);
        if (ignoreFormControls &&
            (keyboard.ContentEditable || keyboard.TargetTagName is "input" or "textarea"))
        {
            return false;
        }

        if (keyboard.MetaKey || (keyboard.Repeat && !action.AllowRepeat))
        {
            return false;
        }

        string code = keyboard.Code.ToLowerInvariant();
        return action.Patterns.Any(pattern =>
            pattern.Codes.Contains(code) &&
            pattern.Ctrl == keyboard.CtrlKey &&
            pattern.Shift == keyboard.ShiftKey &&
            pattern.Alt == keyboard.AltKey);
    }

    private static MisskeyHotkeyPattern ParsePattern(string value)
    {
        bool ctrl = false;
        bool shift = false;
        bool alt = false;
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string token in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    foreach (string code in MisskeyKeyCodes.Resolve(token)) codes.Add(code.ToLowerInvariant());
                    break;
            }
        }

        return new(codes, ctrl, shift, alt);
    }
}
