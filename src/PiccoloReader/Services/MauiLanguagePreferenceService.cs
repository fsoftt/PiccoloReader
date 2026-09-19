using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiLanguagePreferenceService : ILanguagePreferenceService
{
    private const string PreferenceKey = "AppLanguage";

    public string? GetSavedLanguageCode() =>
        Preferences.Default.Get(PreferenceKey, (string?)null);

    public void SaveLanguageCode(string code) =>
        Preferences.Default.Set(PreferenceKey, code);
}
