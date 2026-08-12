namespace ActivityPub.Misskey.Blazor.Overlays;

public interface IMisskeyTransientFeedbackService
{
    event Action? Changed;

    IReadOnlyList<MisskeyTransientFeedbackEntry> Entries { get; }

    IReadOnlyList<MisskeyToastEntry> Toasts { get; }

    Guid ShowSuccess(string announcement);

    Guid ShowToast(string message);

    void Close(Guid id);
}

public sealed record MisskeyTransientFeedbackEntry(Guid Id, string Announcement);

public sealed record MisskeyToastEntry(Guid Id, string Message);

public sealed class MisskeyTransientFeedbackService : IMisskeyTransientFeedbackService
{
    private const int MaximumAnnouncementLength = 200;
    private readonly object sync = new();
    private readonly List<MisskeyTransientFeedbackEntry> entries = [];
    private readonly List<MisskeyToastEntry> toasts = [];

    public event Action? Changed;

    public IReadOnlyList<MisskeyTransientFeedbackEntry> Entries
    {
        get
        {
            lock (sync)
            {
                return entries.ToArray();
            }
        }
    }

    public IReadOnlyList<MisskeyToastEntry> Toasts
    {
        get
        {
            lock (sync)
            {
                return toasts.ToArray();
            }
        }
    }

    public Guid ShowSuccess(string announcement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(announcement);
        if (announcement.Length > MaximumAnnouncementLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(announcement),
                $"A transient announcement cannot exceed {MaximumAnnouncementLength} characters.");
        }

        Guid id = Guid.NewGuid();
        lock (sync)
        {
            entries.Add(new MisskeyTransientFeedbackEntry(id, announcement));
        }

        Changed?.Invoke();
        return id;
    }

    public Guid ShowToast(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > 4_096)
        {
            throw new ArgumentOutOfRangeException(nameof(message));
        }

        Guid id = Guid.NewGuid();
        lock (sync)
        {
            toasts.Add(new MisskeyToastEntry(id, message));
        }

        Changed?.Invoke();
        return id;
    }

    public void Close(Guid id)
    {
        bool removed;
        lock (sync)
        {
            removed = entries.RemoveAll(entry => entry.Id == id) > 0;
            removed = toasts.RemoveAll(entry => entry.Id == id) > 0 || removed;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }
}
