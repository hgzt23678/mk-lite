namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MkWindowButton(
    string Icon,
    Func<Task> OnClick,
    string? Title = null,
    bool Highlighted = false);

public sealed record MkWindowBrowserState(
    bool Maximized,
    double Top,
    double Left,
    double Width,
    double Height,
    int ZIndex);
