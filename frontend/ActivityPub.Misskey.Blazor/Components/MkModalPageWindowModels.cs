using Microsoft.AspNetCore.Components;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MkModalPageWindowMetadata(
    MkPageHeaderMetadata Header,
    bool HideHeader = false,
    IReadOnlyList<MkPageHeaderTab>? Tabs = null,
    IReadOnlyList<MkPageHeaderAction>? Actions = null,
    string? ActiveTab = null,
    Func<string, Task>? TabChanged = null,
    bool DisplayMyAvatar = false);

public sealed class MkModalPageWindowContext
{
    private readonly Func<string, Task> navigate;
    private readonly Func<Task> back;

    internal MkModalPageWindowContext(
        string path,
        Func<string, Task> navigate,
        Func<Task> back)
    {
        Path = path;
        this.navigate = navigate;
        this.back = back;
    }

    public string Path { get; }

    public Task NavigateAsync(string path) => navigate(path);

    public Task BackAsync() => back();
}
