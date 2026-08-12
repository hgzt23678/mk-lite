namespace ActivityPub.Misskey.Blazor.Components;

public enum MkSuperMenuEntryKind
{
    Link,
    Button,
    Route
}

public sealed record MkSuperMenuEntry(
    MkSuperMenuEntryKind Kind,
    string Text,
    string? Icon = null,
    string? Href = null,
    string? Target = null,
    bool Danger = false,
    bool Active = false,
    Func<Task>? Action = null,
    string? To = null)
{
    public static MkSuperMenuEntry Link(string text, string href, string? icon = null, string? target = null, bool danger = false) =>
        new(MkSuperMenuEntryKind.Link, text, icon, href, target, danger);

    public static MkSuperMenuEntry Button(string text, Func<Task> action, string? icon = null, bool danger = false) =>
        new(MkSuperMenuEntryKind.Button, text, icon, Action: action, Danger: danger);

    public static MkSuperMenuEntry Route(
        string text,
        string to,
        string? icon = null,
        bool danger = false,
        bool active = false) =>
        new(MkSuperMenuEntryKind.Route, text, icon, To: to, Danger: danger, Active: active);
}

public sealed record MkSuperMenuGroup(string? Title, IReadOnlyList<MkSuperMenuEntry> Items);
