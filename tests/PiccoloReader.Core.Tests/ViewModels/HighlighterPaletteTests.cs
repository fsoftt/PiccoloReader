using System.Globalization;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class HighlighterPaletteTests
{
    [Fact]
    public void Colors_AreFiveTranslucentArgbHexValues()
    {
        Assert.Equal(5, HighlighterPalette.Colors.Count);
        Assert.All(HighlighterPalette.Colors, c => Assert.Matches("^#66[0-9A-F]{6}$", c));
        Assert.Equal(HighlighterPalette.Colors.Count, HighlighterPalette.Colors.Distinct().Count());
    }

    [Fact]
    public void Default_IsYellowWithAlpha()
    {
        Assert.Equal("#66FFEB3B", HighlighterPalette.Default);
        Assert.Equal(HighlighterPalette.Colors[0], HighlighterPalette.Default);
    }

    [Fact]
    public void ToStored_PrefixesAlpha()
    {
        Assert.Equal("#66FF4081", HighlighterPalette.ToStored("#FF4081"));
    }

    [Fact]
    public void StrokeWidth_DefaultIsInsideSliderRange()
    {
        Assert.InRange(HighlighterPalette.DefaultStrokeWidth, HighlighterPalette.MinStrokeWidth, HighlighterPalette.MaxStrokeWidth);
    }

    [Theory]
    [InlineData("en", "Highlighter")]
    [InlineData("es", "Resaltador")]
    public void HighlighterString_IsLocalized(string culture, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(expected, AppStrings.Highlighter);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
