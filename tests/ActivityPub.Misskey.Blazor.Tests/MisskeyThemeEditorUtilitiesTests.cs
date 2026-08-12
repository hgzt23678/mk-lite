using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyThemeEditorUtilitiesTests
{
    [Fact]
    public void ParsesAndSerializesPinnedThemeValueUnion()
    {
        Assert.Equal("#fff", MisskeyThemeEditorUtilities.FromThemeString("#fff")!.Value);
        MisskeyThemeValue function = MisskeyThemeEditorUtilities.FromThemeString(":darken<10<@panel")!;
        Assert.Equal(MisskeyThemeValueKind.Function, function.Kind);
        Assert.Equal("panel", function.Value);
        Assert.Equal(10, function.Argument);
        Assert.Equal(":darken<10<@panel", MisskeyThemeEditorUtilities.ToThemeString(function));
        Assert.Equal("var(--x)", MisskeyThemeEditorUtilities.FromThemeString("\" var(--x)")!.Value);
        Assert.Null(MisskeyThemeEditorUtilities.FromThemeString(":broken<not-a-number"));
    }

    [Fact]
    public void ConvertsViewModelInStablePropertyThenConstantOrder()
    {
        MisskeyThemeEditorTheme theme = new(
            "id",
            "Theme",
            "Alice",
            null,
            "dark",
            new Dictionary<string, string>
            {
                ["panel"] = "#111",
                ["$accent"] = "#f00",
                ["Xignored"] = "#000",
            });

        IReadOnlyList<KeyValuePair<string, MisskeyThemeValue?>> result =
            MisskeyThemeEditorUtilities.ConvertToViewModel(theme, ["bg", "panel", "panel"]);

        Assert.Equal(["bg", "panel", "$accent"], result.Select(item => item.Key));
        Assert.Null(result[0].Value);
        Assert.Equal("#111", result[1].Value!.Value);
        Assert.Equal(MisskeyThemeValueKind.Color, result[2].Value!.Kind);
    }

    [Fact]
    public void EmitsThemeWithUuidAndDropsNullValues()
    {
        MisskeyThemeEditorTheme theme = MisskeyThemeEditorUtilities.ConvertToMisskeyTheme(
            [
                new KeyValuePair<string, MisskeyThemeValue?>("panel", new(MisskeyThemeValueKind.Color, "#111")),
                new("unused", null),
            ],
            "Theme",
            "Description",
            "Alice",
            "light");

        Assert.True(Guid.TryParse(theme.Id, out _));
        Assert.Equal("#111", theme.Properties["panel"]);
        Assert.DoesNotContain("unused", theme.Properties.Keys);
    }
}
