using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeySearchQueryUtilitiesTests
{
    [Fact]
    public async Task RemovesMentionAndSlashTokensAndResolvesLocalUser()
    {
        string? lookedUp = null;
        MisskeySearchQuery result = await MisskeySearchQueryUtilities.GenerateAsync(
            value: null,
            "hello @alice /local",
            "activitypub.local",
            (id, _) =>
            {
                lookedUp = id;
                return ValueTask.FromResult<string?>("user-1");
            });

        Assert.Equal("hello", result.Query);
        Assert.Equal("alice", lookedUp);
        Assert.Equal("user-1", result.UserId);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task KeepsRemoteHostAndUsesLastMentionTokenLikeUpstream()
    {
        MisskeySearchQuery result = await MisskeySearchQueryUtilities.GenerateAsync(
            value: null,
            "@alice@example.com words @bob@example.net",
            "activitypub.local");

        Assert.Equal("words", result.Query);
        Assert.Equal("bob@example.net", result.Host);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task LocalHostAndDotClearRemoteHostFilter()
    {
        MisskeySearchQuery result = await MisskeySearchQueryUtilities.GenerateAsync(
            value: null,
            "@activitypub.local hello @.",
            "activitypub.local");

        Assert.Equal("hello", result.Query);
        Assert.Null(result.Host);
    }
}
