using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class MediaResourceTests
{
    [Fact]
    public void RemoteMediaSourceTokenIsCanonicalAndRejectsTampering()
    {
        string token = RemoteMediaSourceToken.Create("HTTPS://CDN.Example:443/avatar.png");

        Assert.Equal(RemoteMediaSourceToken.Length, token.Length);
        Assert.Equal(token, RemoteMediaSourceToken.Create("https://cdn.example/avatar.png"));
        Assert.True(RemoteMediaSourceToken.TryNormalize(token.ToUpperInvariant(), out string normalized));
        Assert.Equal(token, normalized);
        Assert.False(RemoteMediaSourceToken.TryNormalize(token[..^1] + "z", out _));
    }

    [Fact]
    public void RemoteActorMediaCacheRejectsStaleLeaseCompletion()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-03T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        string source = "https://cdn.example/avatar.png";
        RemoteActorMediaCacheEntry entry = RemoteActorMediaCacheEntry.CreateClaimed(
            Guid.NewGuid(),
            RemoteActorMediaKind.Avatar,
            source,
            RemoteMediaSourceToken.Create(source),
            "worker-one",
            now,
            now.AddMinutes(1));

        Assert.False(entry.Complete(
            "worker-one",
            Guid.NewGuid(),
            null,
            null,
            now.AddMinutes(1),
            now.AddDays(1)));
        Assert.True(entry.TryClaim("worker-two", now.AddMinutes(1), now.AddMinutes(2)));
        Assert.False(entry.Complete(
            "worker-one",
            Guid.NewGuid(),
            null,
            null,
            now.AddMinutes(1).AddSeconds(1),
            now.AddDays(1)));
    }

    [Fact]
    public void RemoteActorMediaCacheRejectsTokenFromAnotherSource()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-03T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<DomainException>(() => RemoteActorMediaCacheEntry.CreateClaimed(
            Guid.NewGuid(),
            RemoteActorMediaKind.Avatar,
            "https://cdn.example/avatar.png",
            RemoteMediaSourceToken.Create("https://cdn.example/other.png"),
            "worker",
            now,
            now.AddMinutes(1)));
    }

    [Fact]
    public void AvailableMediaCanAdoptTheCommittedObjectVisibility()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse(
            "2026-08-03T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        MediaResource media = MediaResource.Create(
            "https://local.example/users/alice",
            "quarantine/file",
            new string('a', 64),
            "image/png",
            "image.png",
            128,
            Visibility.MentionedOnly,
            createdAt);
        media.MarkAvailable(
            "media/file.png",
            new string('b', 64),
            "image/png",
            128,
            16,
            16,
            null,
            null,
            createdAt.AddSeconds(1));

        media.SetVisibility(Visibility.Public, createdAt.AddSeconds(2));

        Assert.Equal(Visibility.Public, media.Visibility);
        Assert.Equal(createdAt.AddSeconds(2), media.UpdatedAt);
    }

    [Fact]
    public void PendingMediaCannotBecomePublicBeforeProcessingCompletes()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-03T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        MediaResource media = MediaResource.Create(
            "https://local.example/users/alice",
            "quarantine/file",
            new string('a', 64),
            "image/png",
            "image.png",
            128,
            Visibility.MentionedOnly,
            now);

        Assert.Throws<DomainException>(() => media.SetVisibility(Visibility.Public, now.AddSeconds(1)));
    }

    [Fact]
    public void AvailableMediaCacheReferenceRefreshesGarbageCollectionAge()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-03T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        MediaResource media = MediaResource.Create(
            "https://remote.example/users/alice",
            "quarantine/file",
            new string('a', 64),
            "image/png",
            "avatar.png",
            128,
            Visibility.Public,
            now);
        media.MarkAvailable(
            "media/file.png",
            new string('b', 64),
            "image/png",
            128,
            16,
            16,
            null,
            null,
            now.AddSeconds(1));

        media.RefreshCacheReference(now.AddDays(1));

        Assert.Equal(now.AddDays(1), media.UpdatedAt);
        Assert.Throws<DomainException>(() => media.RefreshCacheReference(now));
    }
}
