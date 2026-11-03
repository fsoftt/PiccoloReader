using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Services;

public class MauiConcertModePreferenceService : IConcertModePreferenceService
{
    private const string EnabledKey = "ConcertModeEnabled";

    private readonly object _gate = new();

    // Cached; the constructor loads it off the UI thread.
    private bool? _enabled;

    public MauiConcertModePreferenceService()
    {
        _ = Task.Run(() => _ = Enabled);
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled ??= Preferences.Default.Get(EnabledKey, true);
            }
        }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                Preferences.Default.Set(EnabledKey, value);
            }
        }
    }
}
