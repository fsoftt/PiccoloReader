using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

public class MauiTunerStripPreferenceService : ITunerStripPreferenceService
{
    private const string EnabledKey = "TunerStripEnabled";

    public bool IsEnabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        set => Preferences.Default.Set(EnabledKey, value);
    }
}
