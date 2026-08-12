using Microsoft.AspNetCore.Components.Web;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MisskeyFormDialogItem(string Name, string Type)
{
    public object? DefaultValue { get; init; }

    public string? Label { get; init; }

    public string? Description { get; init; }

    public bool? Required { get; init; }

    public bool Hidden { get; init; }

    public bool Multiline { get; init; }

    public double? Step { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public IReadOnlyList<MisskeyFormDialogOption> Options { get; init; } = [];

    public Func<double, string>? TextConverter { get; init; }

    public string? Content { get; init; }

    public Func<MisskeyFormDialogActionContext, Task>? Action { get; init; }
}

public sealed record MisskeyFormDialogOption(string Label, object? Value);

public sealed record MisskeyFormDialogActionContext(
    MouseEventArgs PointerEvent,
    IDictionary<string, object?> Values);

public sealed record MisskeyFormDialogResult(
    bool Canceled,
    IReadOnlyDictionary<string, object?>? Result = null);
