using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyFocusScrollTests
{
    [Fact]
    public void FocusSkipsSiblingsWithoutTabIndex()
    {
        IReadOnlyList<MisskeyFocusableNode> nodes =
        [new(false, "a"), new(true, "b"), new(false, "c"), new(true, "d")];
        Assert.Equal("b", MisskeyFocusUtilities.Previous(nodes, 3)!.Id);
        Assert.Equal("d", MisskeyFocusUtilities.Next(nodes, 1)!.Id);
        Assert.Null(MisskeyFocusUtilities.Previous(nodes, 0));
        Assert.Null(MisskeyFocusUtilities.Next(nodes, 3));
    }

    [Fact]
    public void ScrollMetricsPreserveContainerAndWindowBranches()
    {
        MisskeyScrollMetrics container = new(100, 400, 500, ElementOffsetTop: 80);
        Assert.Equal(100, MisskeyScrollUtilities.GetScrollPosition(container));
        Assert.False(MisskeyScrollUtilities.IsTopVisible(container));
        Assert.True(MisskeyScrollUtilities.IsBottomVisible(new(0, 400, 400)));
        Assert.True(MisskeyScrollUtilities.IsBottom(new(100, 400, 500), 0));
        Assert.Equal(99_999, MisskeyScrollUtilities.ScrollToBottomTarget());

        MisskeyScrollMetrics window = new(0, 0, 900, WindowScrollY: 300, WindowInnerHeight: 600, HasScrollableContainer: false);
        Assert.Equal(300, MisskeyScrollUtilities.GetScrollPosition(window));
        Assert.True(MisskeyScrollUtilities.IsBottomVisible(window));
        Assert.True(MisskeyScrollUtilities.IsBottom(window));
    }

    [Fact]
    public void TouchStateMatchesV12TouchStartAndTouchEndTransitions()
    {
        MisskeyTouchState state = new();
        state.TouchStart();
        Assert.True(state.IsTouchUsing);
        Assert.True(state.IsScreenTouching);
        state.TouchEnd();
        Assert.True(state.IsTouchUsing);
        Assert.False(state.IsScreenTouching);
    }
}
