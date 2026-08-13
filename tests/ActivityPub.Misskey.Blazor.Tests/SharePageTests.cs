using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Client;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SharePageTests
{
    [Fact]
    public void ComposesShareTextUsingMisskeyTitleAndGoogleNewsRules()
    {
        Assert.Equal(
            "[ Headline ]\nbodyhttps://example.test/article",
            MisskeyShareUtilities.ComposeText("Headline", "Headline.\nbody", "https://example.test/article"));
        Assert.Equal("body", MisskeyShareUtilities.ComposeText(null, "body", null));
        Assert.Equal("https://example.test", MisskeyShareUtilities.ComposeText(null, null, "https://example.test"));
    }

    [Fact]
    public void ProjectsVisibilityAndLocalOnlyQueryWithoutImplicitElevation()
    {
        Assert.Equal(Visibility.Unlisted, MisskeyShareUtilities.ParseVisibility("home"));
        Assert.Equal(Visibility.FollowersOnly, MisskeyShareUtilities.ParseVisibility("followers"));
        Assert.Equal(Visibility.MentionedOnly, MisskeyShareUtilities.ParseVisibility("specified"));
        Assert.Equal(Visibility.Public, MisskeyShareUtilities.ParseVisibility("invalid"));
        Assert.True(MisskeyShareUtilities.ParseLocalOnly("1"));
        Assert.False(MisskeyShareUtilities.ParseLocalOnly("true"));
    }
}
