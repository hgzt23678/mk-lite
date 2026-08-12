using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class OwnershipAndKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForeignActorCannotReplaceObject()
    {
        FederatedObject value = FederatedObject.Create(
            "https://origin.example/objects/1",
            "https://origin.example/users/alice",
            "Note",
            Visibility.Public,
            "{\"type\":\"Note\"}",
            new string('a', 64),
            Now,
            Now);

        Assert.Throws<DomainException>(() => value.Replace(
            "https://attacker.example/users/mallory",
            "Note",
            Visibility.Public,
            "{\"type\":\"Note\"}",
            new string('b', 64),
            Now.AddMinutes(1)));
    }

    [Fact]
    public void RetiredKeyKeepsAnExplicitOverlapWindow()
    {
        ActorKey key = ActorKey.CreateLocal(
            "https://local.example/users/alice#key-1",
            "https://local.example/users/alice",
            "-----BEGIN PUBLIC KEY-----\nfixture\n-----END PUBLIC KEY-----",
            "transit/keys/alice-1",
            Now);
        key.Activate(Now);

        key.Retire(Now.AddDays(30), Now.AddDays(37));

        Assert.Equal(ActorKeyState.Retired, key.State);
        Assert.Equal(Now.AddDays(37), key.ExpiresAt);
    }

    [Fact]
    public void RevokedKeyCannotBeReactivated()
    {
        ActorKey key = ActorKey.CreateRemote(
            "https://remote.example/users/bob#main-key",
            "https://remote.example/users/bob",
            "-----BEGIN PUBLIC KEY-----\nfixture\n-----END PUBLIC KEY-----",
            "rsa-v1_5-sha256",
            Now);
        key.Revoke(Now);

        Assert.Throws<DomainException>(() => key.Activate(Now.AddMinutes(1)));
    }
}
