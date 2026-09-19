using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeAdsPreferenceService : IAdsPreferenceService
{
    public bool Enabled { get; set; }

    public bool GetSupportWithAdsEnabled() => Enabled;

    public void SetSupportWithAdsEnabled(bool enabled) => Enabled = enabled;
}
