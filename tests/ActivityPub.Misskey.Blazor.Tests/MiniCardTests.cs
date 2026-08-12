using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MiniCardTests : BunitContext
{
    public MiniCardTests() => Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());

    [Fact]
    public void InstanceCardPreservesPinnedDomStateClassesAndSameOriginIconBoundary()
    {
        var instance = new FederationInstanceViewModel(
            "instance-id",
            "remote.example",
            "/media/instance.png",
            IsNotResponding: true,
            IsBlocked: true,
            IsSuspended: true,
            SoftwareName: "Mastodon",
            SoftwareVersion: "4.6.2",
            Name: "Remote instance");

        using IRenderedComponent<MkInstanceCardMini> component = Render<MkInstanceCardMini>(parameters => parameters
            .Add(value => value.Instance, instance)
            .Add(value => value.CssClass, "fixture")
            .AddUnmatched("data-contract", "instance-card-mini"));

        IElement root = component.Find("._root_gc11e_1.yellow.red.gray.fixture[data-contract='instance-card-mini']");
        Assert.Equal("/media/instance.png", root.QuerySelector(":scope > img.icon")?.GetAttribute("src"));
        Assert.Equal("Remote instance", root.QuerySelector(":scope > .body > .host")?.TextContent);
        Assert.Equal("remote.example / Mastodon 4.6.2", Normalize(root.QuerySelector(":scope > .body > .sub")?.TextContent));
        Assert.Empty(root.QuerySelectorAll(":scope > .chart"));

        using IRenderedComponent<MkInstanceCardMini> unsafeIcon = Render<MkInstanceCardMini>(parameters => parameters
            .Add(value => value.Instance, instance with
            {
                IconUrl = "https://tracker.invalid/icon.png",
                Name = null,
                SoftwareName = null,
                SoftwareVersion = null,
                IsNotResponding = false,
                IsBlocked = false,
                IsSuspended = false
            }));
        IElement unsafeRoot = unsafeIcon.Find("._root_gc11e_1");
        Assert.Null(unsafeRoot.QuerySelector(":scope > img.icon"));
        Assert.Equal("remote.example", unsafeRoot.QuerySelector(":scope > .body > .host")?.TextContent);
        Assert.Equal("remote.example / ?", Normalize(unsafeRoot.QuerySelector(":scope > .body > .sub")?.TextContent));
        Assert.DoesNotContain("tracker.invalid", unsafeIcon.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserCardPreservesPinnedAvatarNameAccountPreviewAndModerationClasses()
    {
        UserPreviewViewModel user = User();
        using IRenderedComponent<MkUserCardMini> component = Render<MkUserCardMini>(parameters => parameters
            .Add(value => value.User, user)
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "user-card-mini"));

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find("._root_18erp_1.yellow.red.fixture[data-contract='user-card-mini']");
            IElement avatar = root.QuerySelector(":scope > .avatar")!;
            Assert.Equal("SPAN", avatar.TagName);
            Assert.Null(avatar.GetAttribute("href"));
            Assert.Equal("alice-id", avatar.GetAttribute("data-user-preview"));
            Assert.Equal("/media/alice.png", avatar.QuerySelector(":scope > img.inner")?.GetAttribute("src"));
            Assert.NotNull(avatar.QuerySelector(":scope > .indicator.active"));
            Assert.Equal("Alice", root.QuerySelector(":scope > .body > .name > .name")?.TextContent);
            Assert.Equal("@alice@remote.example", root.QuerySelector(":scope > .body > .sub > .acct._monospace")?.TextContent);
            Assert.Empty(root.QuerySelectorAll(":scope > .chart"));
        });
    }

    private static UserPreviewViewModel User() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "alice-id",
        new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@remote.example",
            "Alice",
            "/media/alice.png",
            IsBot: false,
            OnlineStatus: "active"),
        "Existing profile description",
        "/media/banner.png",
        NotesCount: 10,
        FollowingCount: 4,
        FollowersCount: 7,
        IsLocked: false,
        CanFollow: true,
        IsFollowing: true,
        HasPendingFollowRequestFromYou: false,
        IsFollowed: true,
        IsSilenced: true,
        IsSuspended: true);

    private static string? Normalize(string? value) => value is null
        ? null
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
