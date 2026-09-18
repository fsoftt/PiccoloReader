using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class LanguageResolverTests
{
    [Fact]
    public void ResolveLanguageCode_SavedCodePresent_ReturnsSavedCode()
    {
        var result = LanguageResolver.ResolveLanguageCode("es", "en");
        Assert.Equal("es", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsSpanish_ReturnsSpanish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "es");
        Assert.Equal("es", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsEnglish_ReturnsEnglish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "en");
        Assert.Equal("en", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsUnsupportedLanguage_FallsBackToEnglish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "fr");
        Assert.Equal("en", result);
    }
}
