using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyLookupUserTests
{
    [Fact]
    public async Task TriesParsedAccountThenUserIdFallback()
    {
        List<MisskeyUserLookup> calls = [];
        string? id = await MisskeySearchUtilities.LookupUserAsync(
            "@alice@example.com",
            (lookup, _) =>
            {
                calls.Add(lookup);
                return ValueTask.FromResult<string?>(lookup.Username is null ? "user-1" : null);
            });

        Assert.Equal("user-1", id);
        Assert.Equal(2, calls.Count);
        Assert.Equal(new("alice", "example.com"), calls[0]);
        Assert.Equal(new(null, "@alice@example.com"), calls[1]);
    }
}
