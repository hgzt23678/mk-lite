using ActivityPub.Media;

namespace ActivityPub.Media.Tests;

public sealed class MediaTypeSnifferTests
{
    [Theory]
    [InlineData("ffd8ffe00000000000000000", "image/jpeg")]
    [InlineData("89504e470d0a1a0a00000000", "image/png")]
    [InlineData("474946383961000000000000", "image/gif")]
    [InlineData("524946460000000057454250", "image/webp")]
    [InlineData("000000186674797069736f6d", "video/mp4")]
    [InlineData("1a45dfa30000000000000000", "video/webm")]
    [InlineData("4f6767530000000000000000", "audio/ogg")]
    [InlineData("494433000000000000000000", "audio/mpeg")]
    public void DetectsAllowedFormatsFromBytesRatherThanDeclaration(string hex, string expected)
    {
        Assert.Equal(expected, MediaTypeSniffer.Detect(Convert.FromHexString(hex)));
    }

    [Fact]
    public void RejectsUnknownFormat()
    {
        Assert.Throws<InvalidDataException>(() => MediaTypeSniffer.Detect("not a media file"u8));
    }
}
