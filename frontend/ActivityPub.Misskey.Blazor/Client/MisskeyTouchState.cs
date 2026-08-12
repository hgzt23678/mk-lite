namespace ActivityPub.Misskey.Blazor.Client;

public sealed class MisskeyTouchState
{
    public bool IsTouchUsing { get; private set; }

    public bool IsScreenTouching { get; private set; }

    public void TouchStart()
    {
        IsTouchUsing = true;
        IsScreenTouching = true;
    }

    public void TouchEnd()
    {
        IsTouchUsing = true;
        IsScreenTouching = false;
    }
}
