using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyNiraxUtilitiesTests
{
    [Fact]
    public void ResolvesOrderedParametersQueriesHashesAndWildcards()
    {
        IReadOnlyList<MisskeyRouteDefinition> routes =
        [
            new("/@:acct/:page?", "user"),
            new("/docs/:path(*)", "docs", Query: new Dictionary<string, string> { ["lang"] = "language" }, HashParameter: "section"),
        ];

        MisskeyResolvedRoute user = MisskeyNiraxUtilities.Resolve(routes, "/@alice/followers")!;
        Assert.Equal("user", user.Route.Name);
        Assert.Equal("alice", user.Parameters["acct"]);
        Assert.Equal("followers", user.Parameters["page"]);

        MisskeyResolvedRoute docs = MisskeyNiraxUtilities.Resolve(routes, "/docs/a/b?lang=ja#install")!;
        Assert.Equal("a/b", docs.Parameters["path"]);
        Assert.Equal("ja", docs.Parameters["language"]);
        Assert.Equal("install", docs.Parameters["section"]);
    }

    [Fact]
    public void RouteOrderAndSameOriginValidationAreStable()
    {
        IReadOnlyList<MisskeyRouteDefinition> routes =
        [new("/settings/:section?", "settings"), new("/settings/profile", "profile")];
        Assert.Equal("settings", MisskeyNiraxUtilities.Resolve(routes, "/settings/profile")!.Route.Name);
        Assert.Equal("/app/settings", MisskeyNiraxUtilities.NormalizePath("/app/settings"));
        Assert.Throws<ArgumentException>(() => MisskeyNiraxUtilities.NormalizePath("https://evil.example"));
    }
}
