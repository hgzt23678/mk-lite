using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class AvatarTests : BunitContext
{
    [Fact]
    public void PreservesPinnedAvatarDomCatColorLinkIndicatorAndFallthrough()
    {
        int clicks = 0;
        var user = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@remote.example",
            "Alice",
            "/media/proxy/actor/alice/avatar",
            IsBot: false,
            IsCat: true,
            AvatarBlurhash: "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
            OnlineStatus: "active");

        IRenderedComponent<MkAvatar> component = Render<MkAvatar>(parameters => parameters
            .Add(avatar => avatar.User, user)
            .Add(avatar => avatar.CssClass, "fixture-avatar")
            .Add(avatar => avatar.Target, "_blank")
            .Add(avatar => avatar.ShowIndicator, true)
            .Add(avatar => avatar.Clicked, _ => clicks++)
            .AddUnmatched("class", "consumer-avatar")
            .AddUnmatched("style", "width: 64px; height: 64px;")
            .AddUnmatched("data-contract", "avatar"));

        IElement root = component.Find("a");
        Assert.Equal("eiwwqkts _noSelect cat fixture-avatar consumer-avatar", root.ClassName);
        Assert.Equal("color: #979695; width: 64px; height: 64px;", root.GetAttribute("style"));
        Assert.Equal("@alice@remote.example", root.GetAttribute("href"));
        Assert.Equal("@alice@remote.example", root.GetAttribute("title"));
        Assert.Equal("_blank", root.GetAttribute("target"));
        Assert.Equal("alice-id", root.GetAttribute("data-user-preview"));
        Assert.Equal("avatar", root.GetAttribute("data-contract"));
        Assert.Equal("/media/proxy/actor/alice/avatar", root.QuerySelector(":scope > img.inner")?.GetAttribute("src"));
        Assert.Equal("async", root.QuerySelector(":scope > img.inner")?.GetAttribute("decoding"));
        IElement indicator = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > .indicator"));
        Assert.Equal("fzgwjkgc active indicator", indicator.ClassName);
        Assert.Equal("アクティブ", indicator.GetAttribute("title"));

        Assert.DoesNotContain(root.Attributes, attribute =>
            attribute.Name.EndsWith("onclick", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void DisableLinkAndPreviewPreserveSpanBranchAndRemoteUrlBoundary()
    {
        int clicks = 0;
        var user = new NoteAuthorViewModel(
            "remote-id",
            "remote",
            "remote@tracker.example",
            "Remote",
            "https://tracker.example/avatar.gif",
            IsBot: false);

        IRenderedComponent<MkAvatar> component = Render<MkAvatar>(parameters => parameters
            .Add(avatar => avatar.User, user)
            .Add(avatar => avatar.DisableLink, true)
            .Add(avatar => avatar.DisablePreview, true)
            .Add(avatar => avatar.Clicked, _ => clicks++));

        IElement root = component.Find("span.eiwwqkts");
        Assert.Null(root.GetAttribute("data-user-preview"));
        Assert.Empty(component.FindAll("a"));
        Assert.Equal("@remote@tracker.example", root.GetAttribute("title"));
        Assert.Equal("/static-assets/user-unknown.png", root.QuerySelector("img.inner")?.GetAttribute("src"));
        Assert.DoesNotContain("https://tracker.example/avatar.gif", component.Markup, StringComparison.Ordinal);
        root.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void DeviceSettingsApplySquareClassAndStaticMediaQueryWithoutChangingSourcePath()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(
            squareAvatars: true,
            disableAnimatedImages: true));
        var user = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice",
            "Alice",
            "/media/avatar.gif?version=1",
            IsBot: false);

        IRenderedComponent<MkAvatar> component = Render<MkAvatar>(parameters => parameters
            .Add(avatar => avatar.User, user));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("square", component.Find("a").ClassList);
            Assert.Equal("/media/avatar.gif?version=1&static=1", component.Find("img").GetAttribute("src"));
        });
    }

    [Theory]
    [InlineData("online", "オンライン")]
    [InlineData("active", "アクティブ")]
    [InlineData("offline", "オフライン")]
    [InlineData("unexpected", "不明")]
    public void OnlineIndicatorNormalizesStatusAndMergesRootAttributes(string status, string label)
    {
        IRenderedComponent<MkUserOnlineIndicator> component = Render<MkUserOnlineIndicator>(parameters => parameters
            .Add(indicator => indicator.Status, status)
            .AddUnmatched("class", "fixture-indicator")
            .AddUnmatched("data-contract", "online-indicator"));

        IElement root = component.Find("div");
        string expectedStatus = status is "online" or "active" or "offline" ? status : "unknown";
        Assert.Equal($"fzgwjkgc {expectedStatus} fixture-indicator", root.ClassName);
        Assert.Equal(label, root.GetAttribute("title"));
        Assert.Equal(label, root.GetAttribute("aria-label"));
        Assert.Equal("online-indicator", root.GetAttribute("data-contract"));
    }

    private sealed class FixedDeviceState(bool squareAvatars, bool disableAnimatedImages) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            object value = propertyName switch
            {
                "squareAvatars" => squareAvatars,
                "disableShowingAnimatedImages" => disableAnimatedImages,
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
