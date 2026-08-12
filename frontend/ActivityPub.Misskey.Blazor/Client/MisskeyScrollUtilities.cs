namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyScrollMetrics(
    double ScrollTop,
    double ClientHeight,
    double ScrollHeight,
    double WindowScrollY = 0,
    double WindowInnerHeight = 0,
    double ElementOffsetTop = 0,
    bool HasScrollableContainer = true);

public static class MisskeyScrollUtilities
{
    public static double GetScrollPosition(MisskeyScrollMetrics metrics) =>
        metrics.HasScrollableContainer ? metrics.ScrollTop : metrics.WindowScrollY;

    public static bool IsTopVisible(MisskeyScrollMetrics metrics) =>
        GetScrollPosition(metrics) <= metrics.ElementOffsetTop;

    public static bool IsBottomVisible(MisskeyScrollMetrics metrics, double tolerance = 1) =>
        metrics.HasScrollableContainer
            ? metrics.ScrollHeight <= metrics.ClientHeight + Math.Abs(metrics.ScrollTop) + tolerance
            : metrics.ScrollHeight <= metrics.WindowInnerHeight + metrics.WindowScrollY + tolerance;

    public static bool IsBottom(MisskeyScrollMetrics metrics, double asobi = 0)
    {
        double current = metrics.HasScrollableContainer
            ? metrics.ScrollTop + metrics.ClientHeight
            : metrics.WindowScrollY + metrics.WindowInnerHeight;
        double maximum = metrics.ScrollHeight;
        return current >= maximum - asobi;
    }

    public static double ScrollToBottomTarget() => 99_999;
}
