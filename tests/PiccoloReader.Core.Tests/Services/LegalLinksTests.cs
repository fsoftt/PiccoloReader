using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class LegalLinksTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("ES")]
    [InlineData("es-MX")]
    [InlineData("es_AR")]
    public void Spanish_ReturnsSpanishUrls(string code)
    {
        Assert.Equal("https://fsoftt.github.io/PiccoloReader/es/privacy-policy", LegalLinks.GetPrivacyPolicyUrl(code));
        Assert.Equal("https://fsoftt.github.io/PiccoloReader/es/terms-of-service", LegalLinks.GetTermsOfServiceUrl(code));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    public void NonSpanish_ReturnsEnglishUrls(string? code)
    {
        Assert.Equal("https://fsoftt.github.io/PiccoloReader/privacy-policy", LegalLinks.GetPrivacyPolicyUrl(code));
        Assert.Equal("https://fsoftt.github.io/PiccoloReader/terms-of-service", LegalLinks.GetTermsOfServiceUrl(code));
    }
}
