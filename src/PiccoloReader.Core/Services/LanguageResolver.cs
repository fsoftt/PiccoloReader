namespace PiccoloReader.Core.Services;

public static class LanguageResolver
{
    public static string ResolveLanguageCode(string? savedCode, string deviceCode) =>
        savedCode ?? (deviceCode == "es" ? "es" : "en");
}
