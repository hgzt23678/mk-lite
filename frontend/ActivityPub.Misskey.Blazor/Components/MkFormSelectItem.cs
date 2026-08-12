namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MkFormSelectItem(
    string Text,
    string? Value = null,
    IReadOnlyList<MkFormSelectItem>? Items = null,
    bool Disabled = false)
{
    public bool IsGroup => Items is not null;

    public static MkFormSelectItem Option(string value, string text, bool disabled = false) =>
        new(text, value, null, disabled);

    public static MkFormSelectItem Group(string label, IReadOnlyList<MkFormSelectItem> items) =>
        new(label, null, items);
}
