using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;

namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// Typed ports for deterministic Misskey client helpers which do not require a
/// server route. Browser-facing mutation is kept outside these helpers so the
/// same rules can be exercised by unit tests and by the JS interop adapters.
/// </summary>
public static class MisskeyBlurhashUtilities
{
    private const string BlurhashAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    public static string? ExtractAverageColor(string? hash)
    {
        if (hash is null)
        {
            return null;
        }

        int value = 0;
        foreach (char character in hash.Length > 2 ? hash[2..Math.Min(hash.Length, 6)] : string.Empty)
        {
            value = checked(value * 83 + BlurhashAlphabet.IndexOf(character));
        }

        return $"#{value:x6}";
    }
}

public static class MisskeyContainsUtilities
{
    public static bool Contains<T>(
        T parent,
        T? child,
        Func<T, T?> parentOf,
        bool checkSame = true)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(parentOf);
        if (child is null)
        {
            return false;
        }

        if (checkSame && ReferenceEquals(parent, child))
        {
            return true;
        }

        T? node = parentOf(child);
        var visited = new HashSet<T>(ReferenceEqualityComparer.Instance);
        while (node is not null && visited.Add(node))
        {
            if (ReferenceEquals(node, parent))
            {
                return true;
            }

            node = parentOf(node);
        }

        return false;
    }
}

public sealed record MisskeyIntervalOptions(bool Immediate = false, bool AfterMounted = false);

/// <summary>
/// Lifecycle-safe equivalent of scripts/use-interval.ts.  Callers invoke
/// Start after the component has mounted when AfterMounted is true; stopping
/// is idempotent and prevents an old callback from firing after disposal.
/// </summary>
public sealed class MisskeyIntervalController : IDisposable
{
    private readonly object gate = new();
    private readonly Action callback;
    private readonly int intervalMilliseconds;
    private Timer? timer;
    private bool started;
    private bool disposed;
    private long generation;

    public MisskeyIntervalController(Action callback, double interval, MisskeyIntervalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        this.callback = callback;
        intervalMilliseconds = Math.Clamp((int)Math.Round(interval), 0, int.MaxValue);
        Options = options ?? new();
    }

    public MisskeyIntervalOptions Options { get; }

    public bool IsStarted
    {
        get
        {
            lock (gate)
            {
                return started && !disposed;
            }
        }
    }

    public void Start()
    {
        long currentGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                return;
            }

            started = true;
            currentGeneration = ++generation;
        }

        if (Options.Immediate)
        {
            callback();
        }

        lock (gate)
        {
            if (disposed || !started || currentGeneration != generation)
            {
                return;
            }

            timer = new Timer(
                static state =>
                {
                    var context = (IntervalTimerState)state!;
                    context.Owner.Tick(context.Generation);
                },
                new IntervalTimerState(this, currentGeneration),
                intervalMilliseconds,
                intervalMilliseconds);
        }
    }

    public void Stop()
    {
        Timer? oldTimer;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            started = false;
            generation++;
            oldTimer = timer;
            timer = null;
        }

        oldTimer?.Dispose();
    }

    public void Dispose()
    {
        Timer? oldTimer;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            started = false;
            generation++;
            oldTimer = timer;
            timer = null;
        }

        oldTimer?.Dispose();
    }

    private void Tick(long expectedGeneration)
    {
        lock (gate)
        {
            if (disposed || !started || expectedGeneration != generation)
            {
                return;
            }
        }

        callback();
    }

    private sealed record IntervalTimerState(MisskeyIntervalController Owner, long Generation);
}

public sealed record MisskeyPopoutGeometry(
    double ScreenX,
    double ScreenY,
    double Width,
    double Height,
    double Top,
    double Left);

public static class MisskeyPopoutUtilities
{
    public static string BuildUrl(string path, Uri publicBaseUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(publicBaseUri);
        if (publicBaseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The public base URI must use HTTP(S).", nameof(publicBaseUri));
        }

        Uri resolved = Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) &&
                       absolute.Scheme is "http" or "https"
            ? absolute
            : new Uri(publicBaseUri, path.StartsWith('/') ? path : $"/{path}");
        if (resolved.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(resolved.UserInfo))
        {
            throw new ArgumentException("The popout URL is not a safe HTTP(S) URL.", nameof(path));
        }

        string value = resolved.AbsoluteUri;
        return MisskeyUrl.AppendQuery(value, "zen");
    }

    public static string BuildFeatures(MisskeyPopoutGeometry geometry) =>
        $"width={Format(geometry.Width)}, height={Format(geometry.Height)}, top={Format(geometry.Top)}, left={Format(geometry.Left)}";

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record MisskeyStoredAccount(string Id, string Token);

public sealed class MisskeyAccountStore(IMisskeyIndexedStorage storage)
{
    private const string AccountsKey = "accounts";

    public async ValueTask<IReadOnlyList<MisskeyStoredAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MisskeyStoredAccount>? accounts = await storage.GetAsync<MisskeyStoredAccount[]>(AccountsKey, cancellationToken).ConfigureAwait(false);
        return accounts ?? Array.Empty<MisskeyStoredAccount>();
    }

    public async ValueTask<MisskeyStoredAccount?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (await GetAccountsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(account => string.Equals(account.Id, id, StringComparison.Ordinal));
    }

    public async ValueTask AddAsync(string id, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        List<MisskeyStoredAccount> accounts = (await GetAccountsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (accounts.All(account => !string.Equals(account.Id, id, StringComparison.Ordinal)))
        {
            accounts.Add(new(id, token));
            await storage.SetAsync(AccountsKey, accounts.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        List<MisskeyStoredAccount> accounts = (await GetAccountsAsync(cancellationToken).ConfigureAwait(false))
            .Where(account => !string.Equals(account.Id, id, StringComparison.Ordinal))
            .ToList();
        if (accounts.Count == 0)
        {
            await storage.DeleteAsync(AccountsKey, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await storage.SetAsync(AccountsKey, accounts.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed record MisskeyStickySidebarState(
    double? Top,
    double? Bottom,
    double SpacerMarginTop,
    bool IsTop,
    bool IsBottom,
    double LastScrollTop);

public sealed record MisskeyStickySidebarMetrics(
    double ScrollTop,
    double LastScrollTop,
    double ElementHeight,
    double MarginTop,
    double GlobalHeaderHeight,
    double WindowHeight,
    double ElementOffsetTop,
    double ContainerOffsetTop,
    bool IsTop,
    bool IsBottom);

public static class MisskeyStickySidebarUtilities
{
    public static MisskeyStickySidebarState Calculate(MisskeyStickySidebarMetrics metrics)
    {
        double scrollTop = Math.Max(0, metrics.ScrollTop);
        double overflow = Math.Max(0, metrics.GlobalHeaderHeight + metrics.ElementHeight + metrics.MarginTop - metrics.WindowHeight);
        double? top;
        double? bottom;
        double spacer = 0;
        bool isTop = metrics.IsTop;
        bool isBottom = metrics.IsBottom;

        if (scrollTop > metrics.LastScrollTop)
        {
            top = -overflow + metrics.MarginTop + metrics.GlobalHeaderHeight;
            bottom = null;
            isBottom = scrollTop + metrics.WindowHeight >= metrics.ElementOffsetTop + metrics.ElementHeight;
            if (metrics.IsTop)
            {
                isTop = false;
                spacer = Math.Max(0, metrics.GlobalHeaderHeight + metrics.LastScrollTop + metrics.MarginTop - metrics.ContainerOffsetTop);
            }
        }
        else
        {
            double upOverflow = metrics.GlobalHeaderHeight + metrics.ElementHeight + metrics.MarginTop - metrics.WindowHeight;
            top = null;
            bottom = -upOverflow;
            isTop = scrollTop + metrics.MarginTop + metrics.GlobalHeaderHeight <= metrics.ElementOffsetTop;
            if (metrics.IsBottom)
            {
                isBottom = false;
                spacer = metrics.GlobalHeaderHeight + metrics.LastScrollTop + metrics.MarginTop - metrics.ContainerOffsetTop - upOverflow;
            }
        }

        return new(top, bottom, spacer, isTop, isBottom, scrollTop);
    }
}

public sealed record MisskeyResponsiveClassOrder(
    IReadOnlyList<string> Add,
    IReadOnlyList<string> Remove);

public static class MisskeyResponsiveDirectiveUtilities
{
    public static MisskeyResponsiveClassOrder Calculate(
        double width,
        IReadOnlyList<double>? max = null,
        IReadOnlyList<double>? min = null)
    {
        static string MaxClass(double value) => $"max-width_{Format(value)}px";
        static string MinClass(double value) => $"min-width_{Format(value)}px";

        return new(
            (max ?? Array.Empty<double>())
                .Where(value => width <= value)
                .Select(MaxClass)
                .Concat((min ?? Array.Empty<double>()).Where(value => width >= value).Select(MinClass))
                .ToArray(),
            (max ?? Array.Empty<double>())
                .Where(value => width > value)
                .Select(MaxClass)
                .Concat((min ?? Array.Empty<double>()).Where(value => width < value).Select(MinClass))
                .ToArray());
    }

    private static string Format(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

public static class MisskeyPanelDirectiveUtilities
{
    public static string ResolveBackground(string parentBackground, string panelBackground, string panelVariable)
    {
        ArgumentNullException.ThrowIfNull(parentBackground);
        ArgumentNullException.ThrowIfNull(panelBackground);
        ArgumentNullException.ThrowIfNull(panelVariable);
        return string.Equals(parentBackground, panelVariable, StringComparison.Ordinal)
            ? "var(--bg)"
            : "var(--panel)";
    }

    public static string ResolveBorder(string parentBackground, string elementBackground)
    {
        ArgumentNullException.ThrowIfNull(parentBackground);
        ArgumentNullException.ThrowIfNull(elementBackground);
        return string.Equals(parentBackground, elementBackground, StringComparison.Ordinal)
            ? "var(--divider)"
            : elementBackground;
    }
}

public static class MisskeyAnimationDirectiveUtilities
{
    public static (string Opacity, string Transform, string CssClass) BeforeMount() => ("0", "scale(0.9)", "_zoom");

    public static (string Opacity, string Transform) AfterMount() => ("1", "none");
}

public static class MisskeyShareUtilities
{
    public static string ComposeText(string? title, string? text, string? url)
    {
        string normalizedTitle = title?.Trim() ?? string.Empty;
        string normalizedText = text ?? string.Empty;
        string result = string.IsNullOrWhiteSpace(normalizedTitle) ? string.Empty : $"[ {normalizedTitle} ]\n";
        string titlePrefix = $"{normalizedTitle}.\n";
        if (!string.IsNullOrWhiteSpace(normalizedText) && !string.Equals(normalizedTitle, normalizedText, StringComparison.Ordinal))
        {
            result += normalizedText.StartsWith(titlePrefix, StringComparison.Ordinal)
                ? normalizedText[titlePrefix.Length..]
                : $"{normalizedText}\n";
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            result += url;
        }

        return result.Trim();
    }

    public static Visibility ParseVisibility(string? value) => value?.ToLowerInvariant() switch
    {
        "home" => Visibility.Unlisted,
        "followers" => Visibility.FollowersOnly,
        "specified" => Visibility.MentionedOnly,
        _ => Visibility.Public
    };

    public static bool ParseLocalOnly(string? value) => string.Equals(value, "1", StringComparison.Ordinal);
}
