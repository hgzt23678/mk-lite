namespace ActivityPub.Misskey.Blazor.Components;

public enum MkModalType
{
    Auto,
    Popup,
    Dialog,
    DialogTop,
    Drawer
}

public enum MkModalZPriority
{
    Low,
    Middle,
    High
}

public sealed record MkModalAnchor(string X = "center", string Y = "bottom");

public sealed record MkModalContext(string Type, double? MaximumHeight);
