namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// Typed, browser-independent geometry implementation of Misskey v12's
/// <c>calcPopupPosition</c>.  A browser adapter supplies the measured element
/// and viewport values; the policy itself never reads the current URL or host.
/// </summary>
public static class MisskeyPopupPositionUtilities
{
    public static MisskeyPopupPosition Calculate(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(viewport);

        return options.Direction switch
        {
            MisskeyPopupDirection.Top => CalculateTop(content, anchor, options, viewport),
            MisskeyPopupDirection.Bottom => CalculateBottom(content, anchor, options, viewport),
            MisskeyPopupDirection.Left => CalculateLeft(content, anchor, options, viewport),
            MisskeyPopupDirection.Right => CalculateRight(content, anchor, options, viewport),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Direction, "Unknown popup direction."),
        };
    }

    private static MisskeyPopupPosition CalculateTop(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        (double left, double top) = PositionAbove(content, anchor, options, viewport);
        if (top - viewport.PageYOffset < 0)
        {
            (left, top) = PositionBelow(content, anchor, options, viewport);
            return new(left, top, "center top");
        }

        return new(left, top, "center bottom");
    }

    private static MisskeyPopupPosition CalculateBottom(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        (double left, double top) = PositionBelow(content, anchor, options, viewport);
        // v12 keeps bottom placement as the primary direction; viewport clamping
        // is applied by PositionBelow for parity with the upstream helper.
        return new(left, top, "center top");
    }

    private static MisskeyPopupPosition CalculateLeft(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        (double left, double top) = PositionLeft(content, anchor, options, viewport);
        if (left - viewport.PageXOffset < 0)
        {
            (left, top) = PositionRight(content, anchor, options, viewport);
            return new(left, top, "left center");
        }

        return new(left, top, "right center");
    }

    private static MisskeyPopupPosition CalculateRight(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        (double left, double top) = PositionRight(content, anchor, options, viewport);
        // v12 keeps right placement as the primary direction; vertical clamping
        // is applied by PositionRight for parity with the upstream helper.
        return new(left, top, "left center");
    }

    private static (double Left, double Top) PositionAbove(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        double left = anchor is not null
            ? anchor.Rect.Left + viewport.PageXOffset + (anchor.Width / 2)
            : options.X;
        double top = anchor is not null
            ? anchor.Rect.Top + viewport.PageYOffset - content.Height - options.InnerMargin
            : options.Y - content.Height - options.InnerMargin;

        left -= content.Width / 2;
        left = ClampHorizontal(left, content.Width, viewport);
        return (left, top);
    }

    private static (double Left, double Top) PositionBelow(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        double left = anchor is not null
            ? anchor.Rect.Left + viewport.PageXOffset + (anchor.Width / 2)
            : options.X;
        double top = anchor is not null
            ? anchor.Rect.Bottom + viewport.PageYOffset + options.InnerMargin
            : options.Y + options.InnerMargin;

        left -= content.Width / 2;
        left = ClampHorizontal(left, content.Width, viewport);
        return (left, top);
    }

    private static (double Left, double Top) PositionLeft(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        double left = anchor is not null
            ? anchor.Rect.Left + viewport.PageXOffset - content.Width - options.InnerMargin
            : options.X - content.Width - options.InnerMargin;
        double top = anchor is not null
            ? anchor.Rect.Top + viewport.PageYOffset + (anchor.Height / 2)
            : options.Y;

        top -= content.Height / 2;
        top = ClampVertical(top, content.Height, viewport);
        return (left, top);
    }

    private static (double Left, double Top) PositionRight(
        MisskeyPopupContent content,
        MisskeyPopupAnchor? anchor,
        MisskeyPopupPositionOptions options,
        MisskeyPopupViewport viewport)
    {
        double left;
        double top;
        if (anchor is not null)
        {
            left = anchor.Rect.Right + viewport.PageXOffset + options.InnerMargin;
            top = options.Align switch
            {
                MisskeyPopupAlign.Top => anchor.Rect.Top + viewport.PageYOffset + (options.AlignOffset ?? 0),
                // The upstream helper retains the zero sentinel for bottom alignment;
                // preserve that value for DOM-positioning parity.
                MisskeyPopupAlign.Bottom => 0,
                _ => anchor.Rect.Top + viewport.PageYOffset + (anchor.Height / 2) - (content.Height / 2),
            };
        }
        else
        {
            left = options.X + options.InnerMargin;
            top = options.Y - content.Height / 2;
        }

        top = ClampVertical(top, content.Height, viewport);
        return (left, top);
    }

    private static double ClampHorizontal(double left, double width, MisskeyPopupViewport viewport) =>
        left + width - viewport.PageXOffset > viewport.InnerWidth
            ? viewport.InnerWidth - width + viewport.PageXOffset - 1
            : left;

    private static double ClampVertical(double top, double height, MisskeyPopupViewport viewport) =>
        top + height - viewport.PageYOffset > viewport.InnerHeight
            ? viewport.InnerHeight - height + viewport.PageYOffset - 1
            : top;
}

public sealed record MisskeyPopupContent(double Width, double Height);

public sealed record MisskeyPopupRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public sealed record MisskeyPopupAnchor(MisskeyPopupRect Rect)
{
    public double Width => Rect.Width;

    public double Height => Rect.Height;
}

public sealed record MisskeyPopupViewport(
    double InnerWidth,
    double InnerHeight,
    double PageXOffset = 0,
    double PageYOffset = 0);

public sealed record MisskeyPopupPositionOptions(
    MisskeyPopupDirection Direction,
    double InnerMargin,
    MisskeyPopupAlign Align = MisskeyPopupAlign.Center,
    double? AlignOffset = null,
    double X = 0,
    double Y = 0);

public sealed record MisskeyPopupPosition(double Left, double Top, string TransformOrigin);

public enum MisskeyPopupDirection
{
    Top,
    Bottom,
    Left,
    Right,
}

public enum MisskeyPopupAlign
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
}
