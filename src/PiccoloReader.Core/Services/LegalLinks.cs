namespace PiccoloReader.Core.Services;

public static class LegalLinks
{
    private const string BaseUrl = "https://fsoftt.github.io/PiccoloReader/";

    public static string GetPrivacyPolicyUrl(string? languageCode) => BaseUrl + Prefix(languageCode) + "privacy-policy";

    public static string GetTermsOfServiceUrl(string? languageCode) => BaseUrl + Prefix(languageCode) + "terms-of-service";

    private static string Prefix(string? languageCode) =>
        IsSpanish(languageCode) ? "es/" : string.Empty;

    private static bool IsSpanish(string? languageCode) =>
        !string.IsNullOrWhiteSpace(languageCode) &&
        (languageCode.Trim().Equals("es", StringComparison.OrdinalIgnoreCase) ||
         languageCode.Trim().StartsWith("es-", StringComparison.OrdinalIgnoreCase) ||
         languageCode.Trim().StartsWith("es_", StringComparison.OrdinalIgnoreCase));
}
