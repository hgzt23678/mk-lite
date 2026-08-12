using ActivityPub.Misskey.Blazor.Presentation;
using Microsoft.AspNetCore.Components.Web;

namespace ActivityPub.Misskey.Blazor.Overlays;

public enum MisskeyMenuItemKind
{
    Action,
    Link,
    ExternalLink,
    User,
    Switch,
    Parent,
    Pending,
    Label,
    Divider
}

public sealed record MisskeyMenuItem(
    MisskeyMenuItemKind Kind,
    string? Text = null,
    string? Icon = null,
    string? Href = null,
    Func<Task>? Action = null,
    bool Danger = false,
    bool Disabled = false,
    bool Active = false,
    NoteAuthorViewModel? Avatar = null,
    NoteAuthorViewModel? User = null,
    bool Indicate = false,
    bool SwitchValue = false,
    Func<bool, Task>? SwitchChanged = null,
    IReadOnlyList<MisskeyMenuItem>? Children = null,
    string? Target = null,
    string? Download = null,
    Task<MisskeyMenuItem?>? PendingTask = null,
    Func<MouseEventArgs, Task>? MouseAction = null)
{
    public static MisskeyMenuItem Divider { get; } = new(MisskeyMenuItemKind.Divider);

    public static MisskeyMenuItem Link(string text, string icon, string href) =>
        new(MisskeyMenuItemKind.Link, text, icon, href);

    public static MisskeyMenuItem ExternalLink(string text, string icon, string href) =>
        new(MisskeyMenuItemKind.ExternalLink, text, icon, href, Target: "_blank");

    public static MisskeyMenuItem Pending(Task<MisskeyMenuItem?> item) =>
        new(MisskeyMenuItemKind.Pending, PendingTask: item);
}
