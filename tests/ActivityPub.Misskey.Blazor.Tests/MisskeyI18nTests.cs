using ActivityPub.Misskey.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyI18nTests : BunitContext
{
    [Fact]
    public void PreservesTextAndNamedSlotsWithoutRenderingUntrustedMarkup()
    {
        RenderFragment slot = builder => builder.AddContent(0, "Alice");
        IRenderedComponent<MisskeyI18n> component = Render<MisskeyI18n>(parameters => parameters
            .Add(item => item.Src, "Hello {name}!")
            .Add(item => item.Tag, "p")
            .Add(item => item.TextTag, "strong")
            .Add(item => item.Slots, new Dictionary<string, RenderFragment> { ["name"] = slot }));

        Assert.Equal("p", component.Find("p").TagName.ToLowerInvariant());
        Assert.Equal("strong", component.Find("strong").TagName.ToLowerInvariant());
        Assert.Equal("Hello Alice!", component.Find("p").TextContent);
        Assert.DoesNotContain("<script", component.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedPlaceholderIsRetainedAsLiteralTextAndUnsafeTagFallsBack()
    {
        IRenderedComponent<MisskeyI18n> component = Render<MisskeyI18n>(parameters => parameters
            .Add(item => item.Src, "broken {placeholder")
            .Add(item => item.Tag, "script"));

        Assert.Equal("span", component.Find("span").TagName.ToLowerInvariant());
        Assert.Equal("broken {placeholder", component.Find("span").TextContent);
    }
}
