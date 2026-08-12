using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyHotkeyUtilitiesTests
{
    [Fact]
    public void ParsesModifiersAliasesAlternativesAndNoRepeatSyntax()
    {
        IReadOnlyList<MisskeyHotkeyAction> actions = MisskeyHotkeyUtilities.Parse(
            new Dictionary<string, Action<MisskeyKeyboardEvent>>
            {
                ["(Ctrl + S | Enter)"] = _ => { },
            });

        Assert.Equal(2, actions[0].Patterns.Count);
        Assert.True(actions[0].Patterns[0].Ctrl);
        Assert.False(actions[0].AllowRepeat);
        Assert.Contains("enter", actions[0].Patterns[1].Codes);
    }

    [Fact]
    public void MatchesModifiersAndRejectsMetaFormControlsAndDisallowedRepeat()
    {
        MisskeyHotkeyAction action = Assert.Single(MisskeyHotkeyUtilities.Parse(
            new Dictionary<string, Action<MisskeyKeyboardEvent>> { ["(Ctrl + S)"] = _ => { } }));

        Assert.True(MisskeyHotkeyUtilities.Matches(new("KeyS", CtrlKey: true), action));
        Assert.False(MisskeyHotkeyUtilities.Matches(new("KeyS", CtrlKey: true, MetaKey: true), action));
        Assert.False(MisskeyHotkeyUtilities.Matches(new("KeyS", CtrlKey: true, Repeat: true), action));
        Assert.False(MisskeyHotkeyUtilities.Matches(new("KeyS", CtrlKey: true, TargetTagName: "input"), action));
        Assert.False(MisskeyHotkeyUtilities.Matches(new("KeyS", CtrlKey: true, ContentEditable: true), action));
    }
}
