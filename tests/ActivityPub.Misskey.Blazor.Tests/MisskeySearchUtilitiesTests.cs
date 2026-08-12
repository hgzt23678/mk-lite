using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeySearchUtilitiesTests
{
    [Theory]
    [InlineData("", MisskeySearchIntentKind.Empty)]
    [InlineData("@alice", MisskeySearchIntentKind.Account)]
    [InlineData("#misskey", MisskeySearchIntentKind.Tag)]
    [InlineData("2026/08/12", MisskeySearchIntentKind.Date)]
    [InlineData("https://remote.example/notes/1", MisskeySearchIntentKind.RemoteIri)]
    [InlineData("hello world", MisskeySearchIntentKind.Text)]
    public void ClassifiesPinnedSearchForms(string input, MisskeySearchIntentKind kind)
    {
        MisskeySearchIntent result = MisskeySearchUtilities.Parse(input);
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void DateOnlySearchIncludesTheEntireDay()
    {
        MisskeySearchIntent result = MisskeySearchUtilities.Parse("2026-08-12");
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 23, 59, 59, 999, TimeSpan.Zero), result.Date);
    }
}
