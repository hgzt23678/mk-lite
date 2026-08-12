namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MisskeyDialogInput(
    string Type = "text",
    string? Placeholder = null,
    string? Default = null);

public sealed record MisskeyDialogSelect(
    IReadOnlyList<MkFormSelectItem> Items,
    string? Default = null);

public sealed record MisskeyDialogAction(
    string Text,
    Func<Task> Callback,
    bool Primary = false);

public sealed record MisskeyDialogResult(bool Canceled, object? Result = null);
