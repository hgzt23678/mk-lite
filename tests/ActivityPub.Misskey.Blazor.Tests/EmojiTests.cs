using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class EmojiTests : BunitContext
{
    [Fact]
    public void CustomEmojiUsesThePinnedClassesSafeStaticSourceAndAttributeFallthrough()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(
            useOsNativeEmojis: false,
            disableShowingAnimatedImages: true));

        IRenderedComponent<MkEmoji> component = Render<MkEmoji>(parameters => parameters
            .Add(value => value.Emoji, ":party:")
            .Add(value => value.CustomEmojis,
            [
                new EmojiPickerCustomEmoji("party", "/media/party.webp?variant=full", null, [])
            ])
            .Add(value => value.Normal, true)
            .Add(value => value.NoStyle, true)
            .Add(value => value.CssClass, "slot-class")
            .AddUnmatched("class", "fallthrough")
            .AddUnmatched("data-contract", "emoji"));

        component.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement image = component.Find("img.mk-emoji.custom.normal.noStyle.slot-class.fallthrough");
            Assert.Equal("/media/party.webp?variant=full&static=1", image.GetAttribute("src"));
            Assert.Equal(":party:", image.GetAttribute("alt"));
            Assert.Equal(":party:", image.GetAttribute("title"));
            Assert.Equal("async", image.GetAttribute("decoding"));
            Assert.Equal("emoji", image.GetAttribute("data-contract"));
        });
    }

    [Fact]
    public void NativeEmojiSettingUsesTextExceptForReactionIcons()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(
            useOsNativeEmojis: true,
            disableShowingAnimatedImages: false));

        IRenderedComponent<MkEmoji> native = Render<MkEmoji>(parameters => parameters
            .Add(value => value.Emoji, "👍")
            .Add(value => value.Normal, true)
            .Add(value => value.NoStyle, true)
            .AddUnmatched("class", "native-slot"));

        native.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement span = native.Find("span.native-slot");
            Assert.Equal("👍", span.TextContent);
            Assert.DoesNotContain("mk-emoji", span.ClassList);
            Assert.Empty(native.FindAll("img"));
        });

        IRenderedComponent<MkEmoji> reaction = Render<MkEmoji>(parameters => parameters
            .Add(value => value.Emoji, "👍")
            .Add(value => value.IsReaction, true)
            .Add(value => value.Normal, true)
            .Add(value => value.NoStyle, true));

        reaction.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement image = reaction.Find("img.mk-emoji");
            Assert.DoesNotContain("normal", image.ClassList);
            Assert.DoesNotContain("noStyle", image.ClassList);
            Assert.EndsWith("/twemoji/1f44d.svg", image.GetAttribute("src"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MissingOrUnsafeCustomEmojiFallsBackToTheLiteralText()
    {
        IRenderedComponent<MkEmoji> component = Render<MkEmoji>(parameters => parameters
            .Add(value => value.Emoji, ":missing:")
            .Add(value => value.CustomUrl, "https://tracker.invalid/missing.webp")
            .AddUnmatched("class", "literal"));

        AngleSharp.Dom.IElement span = component.Find("span.literal");
        Assert.Equal(":missing:", span.TextContent);
        Assert.DoesNotContain("mk-emoji", span.ClassList);
        Assert.DoesNotContain("tracker.invalid", component.Markup, StringComparison.Ordinal);
    }

    private sealed class FixedDeviceState(
        bool useOsNativeEmojis,
        bool disableShowingAnimatedImages) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName switch
            {
                "useOsNativeEmojis" => useOsNativeEmojis,
                "disableShowingAnimatedImages" => disableShowingAnimatedImages,
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
