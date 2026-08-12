using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SuperMenuTests : BunitContext
{
    [Fact]
    public void RendersPinnedGroupsTitlesAndLinkButtonAndRouteItems()
    {
        int actionCalls = 0;
        IReadOnlyList<MkSuperMenuGroup> definition =
        [
            new("General",
            [
                MkSuperMenuEntry.Link("About", "/about", "fas fa-info-circle"),
                new MkSuperMenuEntry(MkSuperMenuEntryKind.Button, "Reload", "fas fa-redo-alt", Danger: true, Action: () =>
                {
                    actionCalls++;
                    return Task.CompletedTask;
                }),
                MkSuperMenuEntry.Route("Timeline", "/", "fas fa-home")
            ]),
            new(null,
            [
                new MkSuperMenuEntry(MkSuperMenuEntryKind.Link, "External", "fas fa-globe", Href: "https://misskey-hub.net", Target: "_blank")
            ])
        ];

        using IRenderedComponent<MkSuperMenu> component = Render<MkSuperMenu>(parameters => parameters
            .Add(menu => menu.Definition, definition));

        IElement root = component.Find(".rrevdjwu:not(.grid)");
        Assert.Equal(2, root.QuerySelectorAll(":scope > .group").Length);
        Assert.Equal("General", root.QuerySelector(":scope > .group:first-child > .title")?.TextContent.Trim());
        Assert.Equal(3, root.QuerySelectorAll(":scope > .group:first-child > .items > .item").Length);
        Assert.Null(root.QuerySelector(":scope > .group:nth-child(2) > .title"));

        Assert.NotNull(root.QuerySelector("a[href='/about']._button.item > i.fa-fw.fas.fa-info-circle"));
        Assert.Equal("About", root.QuerySelector("a[href='/about'] .text")?.TextContent.Trim());

        IElement button = root.QuerySelector("button.item.danger > i.fa-fw.fas.fa-redo-alt")!.Parent! as IElement ??
            throw new InvalidOperationException("The danger button is missing.");
        button.Click();
        Assert.Equal(1, actionCalls);

        Assert.NotNull(root.QuerySelector("a[href='/'].item > i.fa-fw.fas.fa-home"));
        IElement external = root.QuerySelector("a[href='https://misskey-hub.net']")!;
        Assert.Equal("_blank", external.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", external.GetAttribute("rel"));
    }

    [Fact]
    public void GridModeAppliesPinnedGridClassAndActiveState()
    {
        IReadOnlyList<MkSuperMenuGroup> definition =
        [
            new("Settings",
            [
                new MkSuperMenuEntry(MkSuperMenuEntryKind.Link, "Profile", "fas fa-user", Active: true)
            ])
        ];

        using IRenderedComponent<MkSuperMenu> component = Render<MkSuperMenu>(parameters => parameters
            .Add(menu => menu.Definition, definition)
            .Add(menu => menu.Grid, true));

        IElement root = component.Find(".rrevdjwu.grid");
        Assert.Equal("_button item active", root.QuerySelector("a.item")?.ClassName);
    }

    [Fact]
    public void RouteItemNavigatesOnClick()
    {
        IReadOnlyList<MkSuperMenuGroup> definition =
        [
            new(null, [MkSuperMenuEntry.Route("Timeline", "/timeline", "fas fa-home")])
        ];

        using IRenderedComponent<MkSuperMenu> component = Render<MkSuperMenu>(parameters => parameters
            .Add(menu => menu.Definition, definition));

        component.Find("a[href='/timeline']").Click();
        Assert.Equal("/timeline", new Uri(Services.GetRequiredService<NavigationManager>().Uri).AbsolutePath);
    }
}
