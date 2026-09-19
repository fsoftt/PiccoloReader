using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiAdsPreferenceService : IAdsPreferenceService
{
    private const string PreferenceKey = "SupportWithAds";

    public bool GetSupportWithAdsEnabled() =>
        Preferences.Default.Get(PreferenceKey, false);

    public void SetSupportWithAdsEnabled(bool enabled) =>
        Preferences.Default.Set(PreferenceKey, enabled);
}
