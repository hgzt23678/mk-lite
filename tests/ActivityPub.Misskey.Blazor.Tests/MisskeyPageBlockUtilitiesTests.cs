using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyPageBlockUtilitiesTests
{
    [Fact]
    public void HpmlVariablesInterpolateTypedValuesAndIfBlocksUseState()
    {
        var state = new MisskeyPageState(new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["count"] = 3,
            ["enabled"] = true,
        });
        Assert.Equal("Hello Alice (3)", MisskeyPageBlockUtilities.Interpolate("Hello {{name}} ({{count}})", state));
        Assert.True(MisskeyPageBlockUtilities.IsVisible(new("if", "if", new Dictionary<string, object?> { ["var"] = "enabled" }), state));
        Assert.False(MisskeyPageBlockUtilities.IsVisible(new("if", "if", new Dictionary<string, object?> { ["var"] = "missing" }), state));
    }

    [Fact]
    public void PageBlockUtilitiesKeepSafeImageAndCanvasBounds()
    {
        var block = new MisskeyPageBlock("img", "image", new Dictionary<string, object?>
        {
            ["url"] = "/media/image.png",
            ["comment"] = "preview",
            ["width"] = 8_000,
            ["height"] = 0,
        });
        Assert.True(MisskeyPageBlockUtilities.TryReadImage(block, new Uri("https://activitypub.example/"), out Uri? image, out string alt));
        Assert.Equal("https://activitypub.example/media/image.png", image!.AbsoluteUri);
        Assert.Equal("preview", alt);
        Assert.Equal((4096, 150), MisskeyPageBlockUtilities.CanvasSize(block));
        Assert.False(MisskeyPageBlockUtilities.TryReadImage(block with { Values = new Dictionary<string, object?> { ["url"] = "https://user:pass@evil.example/" } }, new Uri("https://activitypub.example/"), out _, out _));
    }

    [Fact]
    public void PageBlockRegistryAndHeadingRulesMatchV12()
    {
        Assert.Equal(15, MisskeyPageBlockUtilities.SupportedDisplayBlocks.Count);
        Assert.Equal(4, MisskeyPageBlockUtilities.HeadingLevel(3));
        Assert.Equal("kudkigyw primary", MisskeyPageBlockUtilities.ButtonClass(true));
        Assert.Throws<ArgumentException>(() => new MisskeyPageState().Set("bad\nname", true));
    }
}
