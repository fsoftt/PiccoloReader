using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

public sealed class LocalStorageLanguagePreferenceService(BrowserInterop browser) : ILanguagePreferenceService
{
    private const string Key = "piccolo.language";

    public string? GetSavedLanguageCode() => browser.GetLocalStorageSync(Key);

    public void SaveLanguageCode(string code) => browser.SetLocalStorageSync(Key, code);
}
