using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class MisskeyAuthenticationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovedSessionCanBeConsumedOnlyOnce()
    {
        MisskeyAuthSession session = MisskeyAuthSession.Create(
            Guid.NewGuid().ToString("D"),
            "Test client",
            "https://client.example/icon.png",
            null,
            "sampleapp://miauth/callback",
            ["read:account"],
            Now,
            Now.AddMinutes(15));
        Guid tokenId = Guid.NewGuid();
        session.Approve("https://local.example/users/alice", Now.AddSeconds(1));
        session.AttachIssuedToken(tokenId, "protected-token-envelope");

        Assert.Equal("protected-token-envelope", session.Consume(Now.AddSeconds(2)));
        Assert.Equal(MisskeyAuthSessionState.Consumed, session.State);
        Assert.Null(session.EncryptedToken);
        Assert.Throws<DomainException>(() => session.Consume(Now.AddSeconds(3)));
    }

    [Fact]
    public void EmptyPermissionSetRemainsLeastPrivilege()
    {
        Guid sessionId = Guid.NewGuid();
        MisskeyAuthSession session = MisskeyAuthSession.Create(
            Guid.NewGuid().ToString("D"),
            "Read nothing client",
            null,
            null,
            null,
            [],
            Now,
            Now.AddMinutes(15));
        MisskeyAccessToken token = MisskeyAccessToken.Create(
            "https://local.example/users/alice",
            "Read nothing client",
            null,
            null,
            new string('a', 64),
            [],
            Now,
            Now.AddDays(30),
            sessionId);

        Assert.Empty(session.GetPermissions());
        Assert.Empty(token.GetPermissions());
    }

    [Fact]
    public void RevokedOrExpiredTokensCannotBeMarkedUsed()
    {
        MisskeyAccessToken token = MisskeyAccessToken.Create(
            "https://local.example/users/alice",
            "Test client",
            null,
            null,
            new string('b', 64),
            ["write:notes"],
            Now,
            Now.AddHours(1),
            Guid.NewGuid());
        token.Revoke(Now.AddMinutes(5));

        Assert.False(token.IsActive(Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => token.MarkUsed(Now.AddMinutes(6)));
    }
}
