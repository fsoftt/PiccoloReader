using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Services;

public class MauiStylusPreferenceService : IStylusPreferenceService
{
    private const string DrawsDirectlyKey = "StylusDrawsDirectly";

    private readonly object _gate = new();

    // Read on every touch event, so it is cached. The constructor loads it
    // off the UI thread, so the first stylus touch normally finds it ready.
    private bool? _drawsDirectly;

    public MauiStylusPreferenceService()
    {
        _ = Task.Run(() => _ = DrawsDirectly);
    }

    public bool DrawsDirectly
    {
        get
        {
            lock (_gate)
            {
                return _drawsDirectly ??= Preferences.Default.Get(DrawsDirectlyKey, true);
            }
        }
        set
        {
            lock (_gate)
            {
                _drawsDirectly = value;
                Preferences.Default.Set(DrawsDirectlyKey, value);
            }
        }
    }
}
