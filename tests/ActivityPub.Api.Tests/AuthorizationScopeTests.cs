using System.Security.Claims;
using ActivityPub.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class AuthorizationScopeTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task MastodonFavouriteDoesNotAcceptWriteStatusesScope()
    {
        IAuthorizationService authorization = fixture.Services.GetRequiredService<IAuthorizationService>();

        Assert.False((await authorization.AuthorizeAsync(
            Principal("write:statuses", "oauth"),
            resource: null,
            "mastodon.write:favourites")).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            Principal("write:favourites", "oauth"),
            resource: null,
            "mastodon.write:favourites")).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            Principal("write", "oauth"),
            resource: null,
            "mastodon.write:favourites")).Succeeded);
    }

    [Fact]
    public async Task MisskeyAccountAndDrivePoliciesRequireExactPermissions()
    {
        IAuthorizationService authorization = fixture.Services.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal accountReader = MisskeyPrincipal("read:account");
        ClaimsPrincipal driveReader = MisskeyPrincipal("read:drive");
        ClaimsPrincipal driveWriter = MisskeyPrincipal("write:drive");

        Assert.False((await authorization.AuthorizeAsync(accountReader, null, "misskey.write:account")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(accountReader, null, "misskey.read:drive")).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(driveReader, null, "misskey.read:drive")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(driveReader, null, "misskey.write:drive")).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(driveWriter, null, "misskey.write:drive")).Succeeded);
    }

    [Fact]
    public async Task MisskeyTokenPermissionsDoNotBecomeBroadMastodonOrActivityPubScopes()
    {
        IAuthorizationService authorization = fixture.Services.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal noteWriter = MisskeyPrincipal("write:notes");
        ClaimsPrincipal reactionWriter = MisskeyPrincipal("write:reactions");

        Assert.True((await authorization.AuthorizeAsync(noteWriter, null, "mastodon.write")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(noteWriter, null, "mastodon.write:favourites")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(noteWriter, null, "mastodon.write:follows")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(noteWriter, null, "mastodon.write:blocks")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(noteWriter, null, "activitypub.write")).Succeeded);

        Assert.True((await authorization.AuthorizeAsync(reactionWriter, null, "mastodon.write:favourites")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(reactionWriter, null, "mastodon.write")).Succeeded);
    }

    private static ClaimsPrincipal MisskeyPrincipal(string permission)
    {
        string derivedScope = permission.StartsWith("write:", StringComparison.Ordinal)
            ? "activitypub.read activitypub.write"
            : "activitypub.read";
        return new(new ClaimsIdentity(
            [
                new Claim("sub", "test-user"),
                new Claim("scope", derivedScope),
                new Claim("misskey.permission", permission)
            ],
            MisskeyTokenAuthenticationHandler.SchemeName));
    }

    private static ClaimsPrincipal Principal(string scope, string authenticationType) =>
        new(new ClaimsIdentity(
            [new Claim("sub", "test-user"), new Claim("scope", scope)],
            authenticationType));
}
