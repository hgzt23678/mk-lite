using System.Text.Json;
using ActivityPub.Misskey.Blazor.Client;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyClientModuleUtilitiesTests
{
    [Fact]
    public void RuntimeUsesOnlyTheExplicitPublicBaseUri()
    {
        MisskeyClientRuntimeSnapshot runtime = MisskeyClientRuntimeUtilities.FromExplicitConfiguration(
            new("12.119.2-port.1", null, new Uri("https://activitypub.example:8443/")),
            "ja-JP",
            "{\"hello\":\"こんにちは\"}",
            "default",
            debug: false);

        Assert.Equal("activitypub.example:8443", runtime.Host);
        Assert.Equal("https://activitypub.example:8443/api/", runtime.ApiUri.AbsoluteUri);
        Assert.Equal("wss://activitypub.example:8443/streaming", runtime.StreamingUri.AbsoluteUri);
        Assert.Equal("default", runtime.Ui);
        Assert.Throws<InvalidOperationException>(() => MisskeyClientRuntimeUtilities.FromExplicitConfiguration(
            new("12", null), null, null, null, false));
    }

    [Fact]
    public void InstanceEmojiSearchMatchesV12NameAliasAndTagRules()
    {
        MisskeyCustomEmojiSnapshot[] emojis =
        [
            new("party", "/media/party.png", "/media/party.png", "fun", ["celebrate", "party"]),
            new("sad", "/media/sad.png", "/media/sad.png", "faces", ["cry"]),
        ];

        Assert.Equal(["fun", "faces"], MisskeyInstanceUtilities.EmojiCategories(emojis));
        Assert.Equal(["celebrate", "cry", "party"], MisskeyInstanceUtilities.EmojiTags(emojis));
        Assert.Single(MisskeyInstanceUtilities.SearchEmojis(emojis, "cele", new HashSet<string> { "party" }));
        Assert.Empty(MisskeyInstanceUtilities.SearchEmojis(emojis, null));
    }

    [Fact]
    public void NavbarHidesAccountAndUnsupportedCapabilityItemsWithoutChangingOrder()
    {
        IReadOnlyList<MisskeyNavbarItem> items = MisskeyNavbarUtilities.Visible(
            authenticated: true,
            locked: false,
            disabledCapabilities: new HashSet<string>(["drive", "antennas"]));

        Assert.Equal("notifications", items[0].Key);
        Assert.DoesNotContain(items, item => item.Key is "drive" or "antennas" or "followRequests");
        Assert.Contains(items, item => item.Key == "explore");
    }

    [Fact]
    public void CallbackAndReloadPathsAreSameOriginAndFailClosed()
    {
        Assert.Equal("/app/settings", MisskeyActivityPubAuthUtilities.ParseCallback("/app/auth/callback", "/app/settings").SafeReturnPath);
        Assert.Equal("AUTH_RETURN_PATH_INVALID", MisskeyActivityPubAuthUtilities.ParseCallback("/app/auth/callback", "https://evil.example/").ErrorCode);
        Assert.False(MisskeyActivityPubAuthUtilities.ParseCallback("/app/", null).IsCallback);
        Assert.Null(MisskeyReloadUtilities.Create(null).Path);
        Assert.Throws<ArgumentException>(() => MisskeyReloadUtilities.Create("//evil.example"));
        Assert.Throws<ArgumentException>(() => MisskeyReloadUtilities.Create("https://evil.example"));
    }

    [Fact]
    public void EventBusSubscriptionsAreDisposableAndDoNotLeak()
    {
        var bus = new MisskeyEventBus<string>();
        int calls = 0;
        IDisposable subscription = bus.Subscribe(_ => calls++);
        bus.Publish("one");
        subscription.Dispose();
        subscription.Dispose();
        bus.Publish("two");
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NoteCaptureOnlyMutatesDurablePollProjection()
    {
        var note = new ActivityPub.Application.ClientPostView(
            Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, false, string.Empty,
            ActivityPub.Domain.Visibility.Public, null, "https://example.test/notes/1", "https://example.test/notes/1",
            0, 0, 0, false, false, false, false, false, "text", "text", "text", null,
            new ActivityPub.Application.ClientAccountView(
                Guid.NewGuid(), "alice", "alice", "Alice", false, false, false, false, DateTimeOffset.UtcNow,
                "Alice", "https://example.test/@alice", "https://example.test/users/alice", "/avatar", "/header", 0, 0, 0,
                null, [], []), [], [], [], [],
            new ActivityPub.Application.ClientPollView(
                Guid.NewGuid(), null, false, false, 0, 0, false, [],
                [new ActivityPub.Application.ClientPollOptionView("yes", 0)]));

        using JsonDocument body = JsonDocument.Parse("{\"choice\":0}");
        ActivityPub.Application.ClientPostView updated = MisskeyNoteCaptureUtilities.ApplyStreamUpdate(note, "pollVoted", body.RootElement, null);
        Assert.Equal(1, updated.Poll!.Options[0].VotesCount);
        Assert.True(MisskeyNoteCaptureUtilities.IsDeletedEvent("deleted"));
        Assert.False(MisskeyNoteCaptureUtilities.IsDeletedEvent("reacted"));
    }
}
