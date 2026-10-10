using System.Globalization;
using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Tests.Resources;

public class CropStringsTests
{
    [Theory]
    [InlineData("en", "Crop page", "Apply", "Reset")]
    [InlineData("es", "Recortar página", "Aplicar", "Restablecer")]
    public void CropStrings_AreTranslated(string culture, string page, string apply, string reset)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(page, AppStrings.CropPage);
            Assert.Equal(apply, AppStrings.CropApply);
            Assert.Equal(reset, AppStrings.CropReset);
            Assert.False(string.IsNullOrWhiteSpace(AppStrings.CropHint));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
