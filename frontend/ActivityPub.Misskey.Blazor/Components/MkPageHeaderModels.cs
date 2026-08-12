using ActivityPub.Misskey.Blazor.Presentation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MkPageHeaderTab(
    string? Key,
    string Title,
    string? Icon = null,
    bool IconOnly = false,
    string? Action = null,
    Func<MouseEventArgs, Task>? OnClick = null)
{
    public string Identity => Key ?? Action ?? Title;
}

public sealed record MkPageHeaderAction(
    string Text,
    string Icon,
    Func<Task> Handler,
    bool Highlighted = false)
{
    public Func<MouseEventArgs, Task>? PointerHandler { get; init; }

    public Func<ElementReference, MouseEventArgs, Task>? SourcePointerHandler { get; init; }

    public Task InvokeAsync(MouseEventArgs args) => PointerHandler is null
        ? Handler()
        : PointerHandler(args);

    public Task InvokeAsync(ElementReference source, MouseEventArgs args) => SourcePointerHandler is null
        ? InvokeAsync(args)
        : SourcePointerHandler(source, args);
}

public sealed record MkPageHeaderMetadata(
    string Title,
    string? Subtitle = null,
    string? Icon = null,
    NoteAuthorViewModel? Avatar = null,
    NoteAuthorViewModel? UserName = null,
    string? Background = null,
    string AvatarOnlineStatus = "unknown");
