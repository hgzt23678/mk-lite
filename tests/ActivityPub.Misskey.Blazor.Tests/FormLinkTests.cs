using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormLinkTests : BunitContext
{
    [Fact]
    public void InternalBranchPreservesSlotsActiveInlineAndRootAttributeFallthrough()
    {
        IRenderedComponent<FormLink> component = Render<FormLink>(parameters => parameters
            .Add(link => link.To, "/settings/profile")
            .Add(link => link.Active, true)
            .Add(link => link.Inline, true)
            .Add(link => link.Behavior, "browser")
            .Add(link => link.Icon, builder => builder.AddMarkupContent(0, "<i class='fas fa-user'></i>"))
            .Add(link => link.Suffix, builder => builder.AddContent(0, "Account"))
            .AddUnmatched("class", "_formBlock contract-link")
            .AddUnmatched("style", "margin-bottom: 8px;")
            .AddUnmatched("data-contract", "internal")
            .AddUnmatched("disabled", "disabled")
            .AddChildContent("プロフィール"));

        IElement root = component.Find(".ffcbddfc.inline._formBlock.contract-link[data-contract=internal]");
        Assert.Equal("margin-bottom: 8px;", root.GetAttribute("style"));
        Assert.Equal("disabled", root.GetAttribute("disabled"));

        IElement anchor = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > a.main._button.active"));
        Assert.Equal("settings/profile", anchor.GetAttribute("href"));
        Assert.Equal("false", anchor.GetAttribute("data-enhance-nav"));
        Assert.Equal("browser", anchor.GetAttribute("data-misskey-behavior"));
        Assert.False(anchor.HasAttribute("disabled"));
        Assert.NotNull(anchor.QuerySelector(":scope > .icon > .fa-user"));
        Assert.Equal("プロフィール", anchor.QuerySelector(":scope > .text")?.TextContent);
        Assert.Equal("Account", anchor.QuerySelector(":scope > .right > .text")?.TextContent);
        Assert.NotNull(anchor.QuerySelector(":scope > .right > .fa-chevron-right.icon"));
    }

    [Fact]
    public void ExternalBranchPreservesTargetSuffixAndExternalIconWithoutActiveClass()
    {
        IRenderedComponent<FormLink> component = Render<FormLink>(parameters => parameters
            .Add(link => link.To, "https://remote.example/path")
            .Add(link => link.External, true)
            .Add(link => link.Active, true)
            .Add(link => link.Suffix, builder => builder.AddContent(0, "Remote"))
            .AddUnmatched("data-contract", "external")
            .AddChildContent("外部サイト"));

        IElement root = component.Find(".ffcbddfc[data-contract=external]");
        IElement anchor = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > a.main._button"));
        Assert.DoesNotContain("active", anchor.ClassList);
        Assert.Equal("https://remote.example/path", anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", anchor.GetAttribute("rel"));
        Assert.Equal("外部サイト", anchor.QuerySelector(":scope > .text")?.TextContent);
        Assert.Equal("Remote", anchor.QuerySelector(":scope > .right > .text")?.TextContent);
        Assert.NotNull(anchor.QuerySelector(":scope > .right > .fa-external-link-alt.icon"));
        Assert.Null(anchor.GetAttribute("data-misskey-behavior"));
    }

    [Fact]
    public void ClickOnlyUsagePreservesVueRootListenerFallthroughWithoutInventingAHref()
    {
        int clicks = 0;
        IRenderedComponent<FormLink> component = Render<FormLink>(parameters => parameters
            .AddUnmatched(
                "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => clicks++))
            .AddUnmatched("data-contract", "action")
            .AddChildContent("通知をすべて既読にする"));

        IElement root = component.Find(".ffcbddfc[data-contract=action]");
        IElement anchor = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > a.main._button"));
        Assert.False(anchor.HasAttribute("href"));

        root.Click();

        Assert.Equal(1, clicks);
    }

    [Theory]
    [InlineData("javascript:alert(1)", true)]
    [InlineData("https://user:secret@example.test/path", true)]
    [InlineData("//remote.example/path", false)]
    [InlineData("settings/profile", false)]
    public void RejectsUnsafeOrNonContractTargets(string target, bool external)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            Render<FormLink>(parameters => parameters
                .Add(link => link.To, target)
                .Add(link => link.External, external)
                .AddChildContent("unsafe")));

        Assert.Contains(external ? "External form links" : "Internal form links", exception.Message);
    }
}
