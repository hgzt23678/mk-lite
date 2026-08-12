using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyTwoFactorUtilitiesTests
{
    [Fact]
    public void PreservesAsciiBase64urlHexAndStringifySemantics()
    {
        Assert.Equal([65, 66], MisskeyTwoFactorUtilities.Byteify("AB", "ascii"));
        Assert.Equal([0xff, 0xee], MisskeyTwoFactorUtilities.Byteify("_-4", "base64"));
        Assert.Equal([0, 255, 16], MisskeyTwoFactorUtilities.Byteify("00ff10", "hex"));
        Assert.Equal("00ff10", MisskeyTwoFactorUtilities.Hexify([0, 255, 16]));
        Assert.Equal("Aÿ", MisskeyTwoFactorUtilities.Stringify([65, 255]));
    }

    [Fact]
    public void RejectsUnknownOrMalformedEncoding()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MisskeyTwoFactorUtilities.Byteify("a", "utf8"));
        Assert.Throws<FormatException>(() => MisskeyTwoFactorUtilities.Byteify("0", "hex"));
        Assert.Throws<FormatException>(() => MisskeyTwoFactorUtilities.Byteify("_", "base64"));
    }
}
