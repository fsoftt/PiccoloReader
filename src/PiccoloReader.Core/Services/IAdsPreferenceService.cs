namespace PiccoloReader.Core.Services;

public interface IAdsPreferenceService
{
    bool GetSupportWithAdsEnabled();

    void SetSupportWithAdsEnabled(bool enabled);
}
