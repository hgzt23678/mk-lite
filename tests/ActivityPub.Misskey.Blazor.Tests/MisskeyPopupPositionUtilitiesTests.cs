using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyPopupPositionUtilitiesTests
{
    private static readonly MisskeyPopupContent Content = new(100, 40);
    private static readonly MisskeyPopupViewport Viewport = new(800, 600);

    [Fact]
    public void TopUsesAnchorCenterAndFlipsBelowWhenThereIsNoSpace()
    {
        MisskeyPopupPosition position = MisskeyPopupPositionUtilities.Calculate(
            Content,
            new(new MisskeyPopupRect(300, 10, 40, 20)),
            new(MisskeyPopupDirection.Top, 8),
            Viewport);

        Assert.Equal(270, position.Left);
        Assert.Equal(38, position.Top);
        Assert.Equal("center top", position.TransformOrigin);
    }

    [Fact]
    public void HorizontalPlacementClampsToViewportAndFlipsLeftToRight()
    {
        MisskeyPopupPosition position = MisskeyPopupPositionUtilities.Calculate(
            Content,
            new(new MisskeyPopupRect(0, 200, 20, 20)),
            new(MisskeyPopupDirection.Left, 5),
            Viewport);

        Assert.Equal(25, position.Left);
        Assert.Equal(190, position.Top);
        Assert.Equal("left center", position.TransformOrigin);

        MisskeyPopupPosition clamped = MisskeyPopupPositionUtilities.Calculate(
            Content,
            new(new MisskeyPopupRect(790, 200, 20, 20)),
            new(MisskeyPopupDirection.Bottom, 5),
            Viewport);

        Assert.Equal(699, clamped.Left);
    }

    [Fact]
    public void CoordinatePlacementPreservesPageOffsetsAndPinnedBottomAlignTodo()
    {
        MisskeyPopupPosition position = MisskeyPopupPositionUtilities.Calculate(
            Content,
            null,
            new(MisskeyPopupDirection.Right, 4, X: 200, Y: 200),
            new(800, 600, 100, 50));

        Assert.Equal(204, position.Left);
        Assert.Equal(180, position.Top);
        Assert.Equal("left center", position.TransformOrigin);

        MisskeyPopupPosition todo = MisskeyPopupPositionUtilities.Calculate(
            Content,
            new(new MisskeyPopupRect(300, 200, 40, 20)),
            new(MisskeyPopupDirection.Right, 4, MisskeyPopupAlign.Bottom),
            Viewport);

        Assert.Equal(0, todo.Top);
    }
}
