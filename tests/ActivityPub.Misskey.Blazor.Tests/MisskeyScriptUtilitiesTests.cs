using System.Globalization;
using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyScriptUtilitiesTests
{
    [Fact]
    public void ArrayUtilitiesPreserveV12OrderingAndGroupSemantics()
    {
        Assert.Equal([1, 0, 2, 0, 3], MisskeyArrayUtilities.Intersperse(0, [1, 2, 3]));
        Assert.Equal([1, 3], MisskeyArrayUtilities.Difference([1, 2, 3], [2]));
        Assert.Equal([1, 2, 3], MisskeyArrayUtilities.Unique([1, 1, 2, 3, 2]));
        Assert.Equal([[1, 1], [2, 2], [3]], MisskeyArrayUtilities.GroupBy([1, 1, 2, 2, 3], (left, right) => left == right));
        Assert.Equal([1, 3, 6], MisskeyArrayUtilities.CumulativeSum([1d, 2d, 3d]));
        Assert.True(MisskeyArrayUtilities.LessThan([1, 2], [1, 3]));
    }

    [Fact]
    public void StringUtilitiesPreserveNamesDatesAndSafeDecode()
    {
        Assert.Equal("Alice", MisskeyScriptUtilities.GetUserName("Alice", "alice"));
        Assert.Equal("alice", MisskeyScriptUtilities.GetUserName(null, "alice"));
        Assert.Equal("a b", MisskeyScriptUtilities.SafeUriDecode("a%20b"));
        Assert.Equal("%ZZ", MisskeyScriptUtilities.SafeUriDecode("%ZZ"));
        DateTime date = new(2026, 8, 12, 13, 4, 5, DateTimeKind.Unspecified);
        Assert.Equal("2026-08-12 13:04:05 PM", MisskeyScriptUtilities.FormatTimeString(date, "yyyy-MM-dd HH:mm:ss tt", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StaticImageUrlUsesExplicitProxyAndPreservesExistingProxy()
    {
        Uri instance = new("https://local.example");
        string proxied = MisskeyScriptUtilities.GetStaticImageUrl(instance, "https://remote.example/avatar.png");
        Assert.StartsWith("https://local.example/proxy/remote.example/avatar.png?url=", proxied, StringComparison.Ordinal);
        Assert.EndsWith("&static=1", proxied, StringComparison.Ordinal);
        Assert.Equal(
            "https://local.example/proxy/remote.example/avatar.png?static=1",
            MisskeyScriptUtilities.GetStaticImageUrl(instance, "https://local.example/proxy/remote.example/avatar.png"));
        Assert.Throws<ArgumentException>(() => MisskeyScriptUtilities.GetStaticImageUrl(instance, "https://user:pass@remote.example/a"));
    }

    [Fact]
    public void NoteSummaryPreservesReplyRenoteAndSafetyLabels()
    {
        MisskeyNoteSummaryInput note = new(
            ContentWarning: "cw",
            FileCount: 2,
            HasPoll: true,
            HasReply: true,
            Reply: new(Text: "reply"),
            HasRenote: true);

        string summary = MisskeyNoteSummary.Format(note, "deleted", "hidden", "poll", count => $"files:{count}");
        Assert.Equal("cw (files:2) (poll)\n\nRE: reply\n\nRN: ...", summary);
        Assert.Equal("(deleted)", MisskeyNoteSummary.Format(new(IsDeleted: true), "deleted"));
    }

    [Fact]
    public void WordMutePreservesViewerExemptionKeywordAndRegexRules()
    {
        Assert.False(MisskeyWordMute.Matches("alice", "alice", null, "blocked", ["blocked"]));
        Assert.True(MisskeyWordMute.Matches("bob", "alice", "warning", "blocked text", ["warning blocked"]));
        Assert.True(MisskeyWordMute.Matches("bob", "alice", null, "Hello World", ["/hello world/i"]));
        Assert.False(MisskeyWordMute.Matches("bob", "alice", null, "value", ["/[/"]));
    }

    [Fact]
    public void KeyCodeTimeUrlLoginAndTwemojiHelpersPreservePinnedSemantics()
    {
        Assert.Equal(["Enter", "NumpadEnter"], MisskeyKeyCodes.Resolve("ENTER"));
        Assert.Equal(["KeyA"], MisskeyKeyCodes.Resolve("a"));
        Assert.Equal(DateTimeKind.Utc, MisskeyTime.DateUtc([2026, 7, 12]).Kind);
        Assert.True(MisskeyTime.IsBefore(DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow));
        Assert.Equal(3_600_000, (MisskeyTime.Add(DateTime.UnixEpoch, 1, "hour") - DateTime.UnixEpoch).TotalMilliseconds);
        Assert.Equal("a=1&b=x%20y", MisskeyUrl.Query(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x y" }));
        Assert.Equal("https://local.example/a?x=1&loginId=abc", MisskeyLoginId.Add("/a?x=1", "abc", new Uri("https://local.example")));
        Assert.Equal("https://local.example/a?x=1", MisskeyLoginId.Remove("https://local.example/a?x=1&loginId=abc"));
        Assert.Equal("1f44d", MisskeyTwemoji.CharToFileName("👍️"));
        Assert.Equal("/twemoji/1f468-200d-1f4bb.svg", MisskeyTwemoji.CharToFilePath("👨‍💻"));
    }
}
