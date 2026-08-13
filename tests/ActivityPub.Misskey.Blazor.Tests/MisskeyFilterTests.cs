using System.Globalization;
using ActivityPub.Misskey.Blazor.Client;
using ActivityPub.Misskey.Blazor.Client.Filters;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyFilterTests
{
    [Theory]
    [InlineData(null, 0, "?")]
    [InlineData(0d, 0, "0")]
    [InlineData(1024d, 0, "1KB")]
    [InlineData(1536d, 1, "1.5KB")]
    [InlineData(-1048576d, 0, "-1MB")]
    public void BytesPreservesV12Formatting(double? value, int digits, string expected) =>
        Assert.Equal(expected, MisskeyBytesFilter.Format(value, digits));

    [Fact]
    public void NumberUsesCultureAwareGrouping()
    {
        Assert.Equal("1,234,567", MisskeyNumberFilter.Format(1_234_567, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void NotePageRejectsPathInjection()
    {
        Assert.Equal("notes/note-id", MisskeyNoteFilter.Page("note-id"));
        Assert.Throws<ArgumentException>(() => MisskeyNoteFilter.Page("note/id"));
    }

    [Fact]
    public void UserHelpersPreserveNameAcctAndExplicitBaseUri()
    {
        NoteAuthorViewModel user = new(
            "id",
            "alice",
            "alice@example.com",
            "Alice",
            "/avatar.png",
            IsBot: false);

        Assert.Equal("alice@example.com", MisskeyUserFilter.Acct(user));
        Assert.Equal("Alice", MisskeyUserFilter.UserName(user));
        Assert.Equal("https://example.com/@alice@example.com/notes", MisskeyUserFilter.UserPage(
            user,
            "notes",
            new Uri("https://example.com", UriKind.Absolute)));
    }

    [Fact]
    public void BrowserSafeFileTypesExcludeSvgAndJavascript()
    {
        Assert.True(MisskeyFileTypes.IsBrowserSafe("image/png"));
        Assert.True(MisskeyFileTypes.IsBrowserSafe(" VIDEO/MP4 "));
        Assert.False(MisskeyFileTypes.IsBrowserSafe("image/svg+xml"));
        Assert.False(MisskeyFileTypes.IsBrowserSafe("text/html"));
        Assert.False(MisskeyFileTypes.IsBrowserSafe("application/javascript"));
    }
}
