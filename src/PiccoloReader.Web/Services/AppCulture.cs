using System.Globalization;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// Applies the saved (or browser) language to Core's AppStrings, which read CurrentUICulture.
public sealed class AppCulture(ILanguagePreferenceService prefs, BrowserInterop browser)
{
    public string Code { get; private set; } = "en";

    public async Task InitializeAsync()
    {
        var device = (await browser.GetLanguageAsync()).StartsWith("es") ? "es" : "en";
        Apply(LanguageResolver.ResolveLanguageCode(prefs.GetSavedLanguageCode(), device));
    }

    public void Set(string code)
    {
        prefs.SaveLanguageCode(code);
        Apply(code);
    }

    private void Apply(string code)
    {
        Code = code;
        var culture = new CultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
