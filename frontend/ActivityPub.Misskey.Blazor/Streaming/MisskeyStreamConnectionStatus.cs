namespace ActivityPub.Misskey.Blazor.Streaming;

public interface IMisskeyStreamConnectionStatus
{
    event EventHandler? Disconnected;

    bool IsDisconnected { get; }

    void ReportConnected();

    void ReportDisconnected();

    void Reset();
}

public sealed class MisskeyStreamConnectionStatus : IMisskeyStreamConnectionStatus
{
    private readonly Lock gate = new();
    private bool isDisconnected;

    public event EventHandler? Disconnected;

    public bool IsDisconnected
    {
        get
        {
            lock (gate)
            {
                return isDisconnected;
            }
        }
    }

    public void ReportConnected()
    {
        lock (gate)
        {
            isDisconnected = false;
        }
    }

    public void ReportDisconnected()
    {
        EventHandler? handler;
        lock (gate)
        {
            if (isDisconnected)
            {
                return;
            }

            isDisconnected = true;
            handler = Disconnected;
        }

        handler?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        lock (gate)
        {
            isDisconnected = false;
        }
    }
}
