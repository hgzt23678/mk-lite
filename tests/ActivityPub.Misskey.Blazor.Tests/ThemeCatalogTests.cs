using ActivityPub.Misskey.Blazor.State;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ThemeCatalogTests
{
    private readonly ThemeCatalog catalog = new();

    [Fact]
    public void ContainsEveryMisskeyTwelveTheme()
    {
        Assert.Equal(20, catalog.Themes.Count);
        Assert.Equal(18, catalog.Themes.Count(theme => theme.Selectable));
        Assert.Equal(20, catalog.Themes.Select(theme => theme.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCompiledThemeContainsCompleteOpaqueSurfaceInputs()
    {
        foreach (ThemeDefinition theme in catalog.Themes)
        {
            Assert.True(theme.Properties.Count >= 80, $"{theme.SourceFile} has an incomplete property set.");
            Assert.False(string.IsNullOrWhiteSpace(theme.Properties["bg"]));
            Assert.False(string.IsNullOrWhiteSpace(theme.Properties["panel"]));
            Assert.Matches("^rgb\\([0-9]+, [0-9]+, [0-9]+\\)$", theme.Properties["popup"]);
            Assert.Equal("solid 1px var(--divider)", theme.Properties["panelBorder"]);
            Assert.Matches("^[a-f0-9]{64}$", theme.SourceSha256);
            Assert.True(theme.Base is "light" or "dark");
        }
    }

    [Fact]
    public void UnknownThemeIdIsRejected()
    {
        Assert.Throws<KeyNotFoundException>(() => catalog.GetRequired("not-a-theme"));
    }
}
