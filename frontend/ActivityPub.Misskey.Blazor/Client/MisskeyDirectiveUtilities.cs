namespace ActivityPub.Misskey.Blazor.Client;

public sealed class MisskeyAppearDirectiveState : IDisposable
{
    private readonly Action callback;
    private bool disposed;

    public MisskeyAppearDirectiveState(Action callback)
    {
        this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public void IntersectionChanged(bool isIntersecting)
    {
        if (!disposed && isIntersecting) callback();
    }

    public void Dispose() => disposed = true;
}

public static class MisskeyClickAnimationUtilities
{
    // v12's click-anime directive is intentionally commented out upstream.
    // Keeping this explicit no-op avoids adding listeners that the oracle does
    // not install and gives callers a safe feature test.
    public static bool IsEnabled(bool animationSetting) => false;
}

public static class MisskeyFollowAppendUtilities
{
    public static bool ShouldStickToBottom(double scrollTop, double clientHeight, double scrollHeight, double threshold = 32) =>
        scrollTop + clientHeight > scrollHeight - threshold;

    public static double FollowResize(double scrollHeight, bool stickToBottom) => stickToBottom ? scrollHeight : double.NaN;
}

public sealed class MisskeyElementSizeDirectiveState(Action<double, double> changed) : IDisposable
{
    private bool disposed;

    public void Resize(double width, double height)
    {
        if (!disposed) changed(Math.Max(0, width), Math.Max(0, height));
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            changed(0, 0);
        }
    }
}

public sealed record MisskeyTooltipDirectiveOptions(
    string Text,
    int DelayMilliseconds = 100,
    string Direction = "top",
    bool AsMfm = false,
    bool Dialog = false);

public sealed class MisskeyTooltipDirectiveState(MisskeyTooltipDirectiveOptions options, Action<bool> showingChanged) : IDisposable
{
    private bool showing;
    private bool disposed;

    public MisskeyTooltipDirectiveOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public bool IsShowing => showing;

    public void Open()
    {
        if (!disposed && !showing && !string.IsNullOrWhiteSpace(Options.Text))
        {
            showing = true;
            showingChanged(true);
        }
    }

    public void Close()
    {
        if (showing)
        {
            showing = false;
            showingChanged(false);
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Close();
        }
    }
}

public static class MisskeyDirectiveRegistry
{
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "userPreview", "user-preview", "size", "get-size", "ripple", "tooltip", "hotkey", "appear", "anim", "click-anime", "panel", "adaptive-border"
    };

    public static bool Contains(string name) => !string.IsNullOrWhiteSpace(name) && Names.Contains(name);
}

public sealed record MisskeyLoginGateDecision(bool Allowed, bool ShouldOpenSignIn, string? ReturnPath, string? ErrorCode);

public static class MisskeyLoginGateUtilities
{
    public static MisskeyLoginGateDecision Require(bool authenticated, string? path)
    {
        if (authenticated)
        {
            return new(true, false, null, null);
        }

        if (path is null)
        {
            return new(false, true, null, "AUTH_REQUIRED");
        }

        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Any(char.IsControl))
        {
            return new(false, true, null, "AUTH_RETURN_PATH_INVALID");
        }

        return new(false, true, path, "AUTH_REQUIRED");
    }
}

public sealed record MisskeySuspendedAccountNotice(string TitleKey, string DescriptionKey)
{
    public static MisskeySuspendedAccountNotice Default { get; } = new(
        "yourAccountSuspendedTitle",
        "yourAccountSuspendedDescription");
}

public sealed class MisskeyReactionPickerState : IDisposable
{
    private readonly Action<string> chosen;
    private readonly Action closed;
    private bool disposed;

    public MisskeyReactionPickerState(Action<string> chosen, Action closed)
    {
        this.chosen = chosen ?? throw new ArgumentNullException(nameof(chosen));
        this.closed = closed ?? throw new ArgumentNullException(nameof(closed));
    }

    public bool Showing { get; private set; }

    public void Show()
    {
        if (!disposed) Showing = true;
    }

    public void Choose(string reaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reaction);
        if (!disposed && Showing)
        {
            chosen(reaction);
            Showing = false;
        }
    }

    public void Close()
    {
        if (!disposed && Showing)
        {
            Showing = false;
            closed();
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Showing = false;
        }
    }
}
