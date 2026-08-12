using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyDirectiveUtilitiesTests
{
    [Fact]
    public void AppearAndSizeStatesStopCallbacksAfterDispose()
    {
        int appearances = 0;
        using var appear = new MisskeyAppearDirectiveState(() => appearances++);
        appear.IntersectionChanged(true);
        appear.Dispose();
        appear.IntersectionChanged(true);
        Assert.Equal(1, appearances);

        var sizes = new List<(double Width, double Height)>();
        var size = new MisskeyElementSizeDirectiveState((width, height) => sizes.Add((width, height)));
        size.Resize(-1, 20);
        size.Dispose();
        size.Resize(30, 40);
        Assert.Equal([(0, 20), (0, 0)], sizes);
    }

    [Fact]
    public void FollowAppendUsesThePinnedBottomThreshold()
    {
        Assert.True(MisskeyFollowAppendUtilities.ShouldStickToBottom(900, 100, 1_000));
        Assert.False(MisskeyFollowAppendUtilities.ShouldStickToBottom(800, 100, 1_000));
        Assert.Equal(1_000, MisskeyFollowAppendUtilities.FollowResize(1_000, true));
        Assert.True(double.IsNaN(MisskeyFollowAppendUtilities.FollowResize(1_000, false)));
    }

    [Fact]
    public void TooltipStateIsIdempotentAndRegistryMatchesUpstreamNames()
    {
        var changes = new List<bool>();
        using var tooltip = new MisskeyTooltipDirectiveState(new("hello", Direction: "right"), changes.Add);
        tooltip.Open();
        tooltip.Open();
        tooltip.Close();
        tooltip.Close();
        Assert.Equal([true, false], changes);
        Assert.Equal(12, MisskeyDirectiveRegistry.Names.Count);
        Assert.True(MisskeyDirectiveRegistry.Contains("adaptive-border"));
        Assert.False(MisskeyClickAnimationUtilities.IsEnabled(true));
    }

    [Fact]
    public void PleaseLoginGateNeverAcceptsAnExternalReturnPath()
    {
        Assert.True(MisskeyLoginGateUtilities.Require(true, null).Allowed);
        MisskeyLoginGateDecision decision = MisskeyLoginGateUtilities.Require(false, "/app/settings");
        Assert.False(decision.Allowed);
        Assert.True(decision.ShouldOpenSignIn);
        Assert.Equal("/app/settings", decision.ReturnPath);
        Assert.Equal("AUTH_RETURN_PATH_INVALID", MisskeyLoginGateUtilities.Require(false, "https://evil.example").ErrorCode);
        Assert.Equal("yourAccountSuspendedTitle", MisskeySuspendedAccountNotice.Default.TitleKey);
    }

    [Fact]
    public void ReactionPickerEmitsOneChoiceAndClosesExactlyOnce()
    {
        var chosen = new List<string>();
        int closes = 0;
        using var picker = new MisskeyReactionPickerState(chosen.Add, () => closes++);
        picker.Show();
        picker.Choose(":party:");
        picker.Choose(":second:");
        picker.Close();
        Assert.Equal([":party:"], chosen);
        Assert.Equal(0, closes);
        picker.Show();
        picker.Close();
        Assert.Equal(1, closes);
    }
}
