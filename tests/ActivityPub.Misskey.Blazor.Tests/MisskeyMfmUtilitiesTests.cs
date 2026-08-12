using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyMfmUtilitiesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public void ExtractMentionsWalksNestedNodesWithoutDeduplicatingUpstreamResults()
    {
        IReadOnlyList<MfmNode> nodes = Parse("""
            [{"type":"quote","props":{},"children":[
              {"type":"mention","props":{"username":"alice","host":null},"children":null},
              {"type":"mention","props":{"username":"alice","host":null},"children":null}
            ]}]
            """);

        IReadOnlyList<JsonElement> mentions = MisskeyMfmUtilities.ExtractMentions(nodes);
        Assert.Equal(2, mentions.Count);
        Assert.Equal("alice", mentions[0].GetProperty("username").GetString());
    }

    [Fact]
    public void ExtractUrlsPreservesFirstHashVariantAndHonorsSilentLinks()
    {
        IReadOnlyList<MfmNode> nodes = Parse("""
            [
              {"type":"url","props":{"url":"https://a.test/#one"},"children":null},
              {"type":"url","props":{"url":"https://a.test/#two"},"children":null},
              {"type":"link","props":{"url":"https://b.test/","silent":true},"children":null},
              {"type":"link","props":{"url":"https://c.test/#x","silent":false},"children":null}
            ]
            """);

        Assert.Equal(["https://a.test/#one", "https://c.test/#x"], MisskeyMfmUtilities.ExtractUrls(nodes));
        Assert.Equal(
            ["https://a.test/#one", "https://b.test/", "https://c.test/#x"],
            MisskeyMfmUtilities.ExtractUrls(nodes, respectSilentFlag: false));
    }

    [Fact]
    public void MfmTagsAndTimezonesPreservePinnedCatalogOrder()
    {
        Assert.Equal("tada", MisskeyMfmTags.Tags[0]);
        Assert.Equal("rotate", MisskeyMfmTags.Tags[^1]);
        Assert.Equal(16, MisskeyMfmTags.Tags.Count);
        Assert.Equal("Asia/Tokyo", MisskeyTimezones.Values[2].Name);
        Assert.Equal(-480, MisskeyTimezones.Values[^1].OffsetMinutes);
    }

    private static MfmNode[] Parse(string json) =>
        JsonSerializer.Deserialize<MfmNode[]>(json, JsonOptions) ?? [];
}
