namespace ActivityPub.Misskey.Blazor.State;

public interface IPageMetadataState
{
    string? Background { get; }

    event EventHandler? Changed;

    void SetBackground(string? background);
}

/// <summary>
/// Per-circuit page metadata bridge. The current page header publishes the same background
/// value that the v12 universal shell applies to its main element; it is deliberately scoped
/// so metadata from one authenticated circuit cannot bleed into another.
/// </summary>
public sealed class PageMetadataState : IPageMetadataState
{
    private string? background;

    public string? Background => background;

    public event EventHandler? Changed;

    public void SetBackground(string? background)
    {
        string? normalized = string.IsNullOrWhiteSpace(background) ? null : background.Trim();
        if (string.Equals(this.background, normalized, StringComparison.Ordinal))
        {
            return;
        }

        this.background = normalized;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
