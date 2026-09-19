using System.Globalization;
using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Tests.Resources;

public class AppStringsTests
{
    [Fact]
    public void Library_EnglishCulture_ReturnsEnglishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Library", AppStrings.Library);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Library_SpanishCulture_ReturnsSpanishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es");
            Assert.Equal("Biblioteca", AppStrings.Library);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void SearchFoldersPlaceholder_SpanishCulture_ReturnsSpanishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es");
            Assert.Equal("Buscar carpetas", AppStrings.SearchFoldersPlaceholder);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void PageIndicatorFormat_UsedWithStringFormat_ProducesExpectedEnglishText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var text = string.Format(AppStrings.PageIndicatorFormat, 2, 5);
            Assert.Equal("Page 2 of 5", text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
