using System.Globalization;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NumberDiffTests : BunitContext
{
    [Theory]
    [InlineData(1234, "isPlus", "+1,234")]
    [InlineData(-1234, "isMinus", "-1,234")]
    [InlineData(0, "isZero", "0")]
    public void PreservesUpstreamDomClassesSlotsAndLocaleFormatting(
        double value,
        string stateClass,
        string expectedText)
    {
        using var culture = new CultureScope("en-US");
        IRenderedComponent<MkNumberDiff> component = Render<MkNumberDiff>(parameters => parameters
            .Add(diff => diff.Value, value)
            .Add(diff => diff.Before, "(")
            .Add(diff => diff.After, ")")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "number-diff"));

        IElement root = component.Find("span.ceaaebcd");
        Assert.Equal($"ceaaebcd {stateClass} fixture", root.ClassName);
        Assert.Equal("number-diff", root.GetAttribute("data-contract"));
        Assert.Equal($"({expectedText})", root.TextContent);
    }

    [Fact]
    public void MatchesNumberToLocaleStringDefaultFractionLimitAndNegativeZero()
    {
        using var culture = new CultureScope("en-US");
        Assert.Equal("+1,234.568", RenderText(1234.5678));
        Assert.Equal("+0.001", RenderText(0.0005));
        Assert.Equal("-0", RenderText(-0d));
    }

    private string RenderText(double value) => Render<MkNumberDiff>(parameters => parameters
        .Add(diff => diff.Value, value)).Find("span.ceaaebcd").TextContent;

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
