using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Services;

public class MauiStylusPreferenceService : IStylusPreferenceService
{
    private const string DrawsDirectlyKey = "StylusDrawsDirectly";

    // Read on every touch event, so it is cached after the first read.
    private bool? _drawsDirectly;

    public bool DrawsDirectly
    {
        get => _drawsDirectly ??= Preferences.Default.Get(DrawsDirectlyKey, true);
        set
        {
            _drawsDirectly = value;
            Preferences.Default.Set(DrawsDirectlyKey, value);
        }
    }
}
