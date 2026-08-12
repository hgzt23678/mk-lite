using System.Text.Json;
using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyFrontendRuntimeUtilitiesTests
{
    private const string Json = """
        {
          "enabled": true,
          "instanceName": "local.example",
          "authority": "https://identity.example/",
          "clientId": "activitypub-web",
          "scopes": ["openid", "profile"],
          "redirectUri": "https://local.example/app/auth/callback",
          "postLogoutRedirectUri": "https://local.example/app/",
          "sourceUrl": "https://source.example/frontend",
          "capabilities": {
            "publicTimeline": true, "localTimeline": true, "homeTimeline": true,
            "compose": true, "favourite": true, "renote": true, "mute": true,
            "mediaUpload": false, "notifications": false, "streaming": false
          }
        }
        """;

    [Fact]
    public void AcceptsSafeConfigAndNormalizesAuthority()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        MisskeyFrontendRuntimeConfig config = MisskeyFrontendRuntimeUtilities.Validate(
            document.RootElement,
            new Uri("https://local.example"));

        Assert.Equal("https://identity.example/", config.Authority.AbsoluteUri);
        Assert.Equal("https://source.example/frontend", config.SourceUrl!.AbsoluteUri);
        Assert.False(config.Capabilities.Streaming);
    }

    [Theory]
    [InlineData("http://identity.example/")]
    [InlineData("https://attacker.example/app/auth/callback")]
    [InlineData("https://local.example/callback")]
    public void RejectsUnsafeAuthorityOrCallback(string replacement)
    {
        using JsonDocument original = JsonDocument.Parse(Json);
        Dictionary<string, object?> fields = new()
        {
            ["enabled"] = true,
            ["instanceName"] = "local.example",
            ["authority"] = replacement,
            ["clientId"] = "activitypub-web",
            ["scopes"] = new[] { "openid" },
            ["redirectUri"] = replacement.Contains("callback", StringComparison.Ordinal) ? replacement : "https://local.example/app/auth/callback",
            ["postLogoutRedirectUri"] = "https://local.example/app/",
            ["capabilities"] = original.RootElement.GetProperty("capabilities").Clone(),
        };
        using JsonDocument changed = JsonDocument.Parse(JsonSerializer.Serialize(fields));

        Assert.Throws<ArgumentException>(() => MisskeyFrontendRuntimeUtilities.Validate(
            changed.RootElement,
            new Uri("https://local.example")));
    }

    [Fact]
    public void RejectsIncompleteCapabilityDeclaration()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        Dictionary<string, object?> fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(Json)!;
        Dictionary<string, object?> capabilities = JsonSerializer.Deserialize<Dictionary<string, object?>>(fields["capabilities"]!.ToString()!)!;
        capabilities.Remove("compose");
        fields["capabilities"] = capabilities;
        using JsonDocument changed = JsonDocument.Parse(JsonSerializer.Serialize(fields));

        Assert.Throws<ArgumentException>(() => MisskeyFrontendRuntimeUtilities.Validate(
            changed.RootElement,
            new Uri("https://local.example")));
    }
}
