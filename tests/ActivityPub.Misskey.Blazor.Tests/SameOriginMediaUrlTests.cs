using ActivityPub.Misskey.Blazor.Security;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SameOriginMediaUrlTests
{
    [Theory]
    [InlineData("/media/123", "/media/123")]
    [InlineData("/media/proxy/object/token?variant=preview", "/media/proxy/object/token?variant=preview")]
    [InlineData("/static-assets/favicon.png", "/static-assets/favicon.png")]
    [InlineData("https://tracker.example/image.png", null)]
    [InlineData("http://tracker.example/image.png", null)]
    [InlineData("//tracker.example/image.png", null)]
    [InlineData("/media\\image.png", null)]
    [InlineData("/media/image.png\nbackground:red", null)]
    [InlineData("relative/image.png", null)]
    public void NormalizeOnlyAcceptsSameOriginRootedPaths(string value, string? expected)
    {
        Assert.Equal(expected, SameOriginMediaUrl.Normalize(value));
    }

    [Fact]
    public void CssBackgroundImageEscapesApostropheWithoutAllowingASecondDeclaration()
    {
        Assert.Equal(
            "background-image: url('/media/a%27);color:red')",
            SameOriginMediaUrl.CssBackgroundImage("/media/a');color:red"));
    }
}
