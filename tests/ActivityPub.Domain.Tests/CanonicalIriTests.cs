using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class CanonicalIriTests
{
    [Fact]
    public void WebOriginCanPermitHttpForAnExplicitDevelopmentBoundary()
    {
        string origin = CanonicalIri.RequireWebOrigin("http://activitypub/", "origin", requireHttps: false);

        Assert.Equal("http://activitypub", origin);
    }

    [Theory]
    [InlineData("http://activitypub/path")]
    [InlineData("http://activitypub/?query=true")]
    [InlineData("http://activitypub/#fragment")]
    public void WebOriginRejectsNonOriginIris(string value)
    {
        Assert.Throws<DomainException>(() => CanonicalIri.RequireWebOrigin(value, "origin", requireHttps: false));
    }

    [Fact]
    public void HttpsOriginStillRejectsPlainHttp()
    {
        Assert.Throws<DomainException>(() => CanonicalIri.RequireHttpsOrigin("http://activitypub", "origin"));
    }
}
