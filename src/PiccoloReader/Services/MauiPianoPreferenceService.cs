using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Services;

public class MauiPianoPreferenceService : IPianoPreferenceService
{
    private const string InstrumentKey = "PianoInstrument";
    private const string WhiteKeysPortraitKey = "PianoWhiteKeysPortrait";
    private const string WhiteKeysLandscapeKey = "PianoWhiteKeysLandscape";
    private const string FirstWhiteKeyKey = "PianoFirstWhiteKey";
    private const string FirstWhiteKeyLandscapeKey = "PianoFirstWhiteKeyLandscape";
    private const string LabelsKey = "PianoLabels";
    private const string StripWhiteKeysKey = "PianoStripWhiteKeys";
    private const string StripFirstWhiteKeyKey = "PianoStripFirstWhiteKey";

    private readonly object _gate = new();
    private PianoSettings? _pending;

    public PianoSettings Load()
    {
        // A save still on its way to disk is written first, so a read never sees older values.
        Flush();
        var defaults = new PianoSettings();
        var labels = Preferences.Default.Get(LabelsKey, (int)defaults.Labels);
        return new PianoSettings
        {
            InstrumentId = Preferences.Default.Get(InstrumentKey, defaults.InstrumentId),
            PortraitWhiteKeys = Preferences.Default.Get(WhiteKeysPortraitKey, defaults.PortraitWhiteKeys),
            LandscapeWhiteKeys = Preferences.Default.Get(WhiteKeysLandscapeKey, defaults.LandscapeWhiteKeys),
            PortraitFirstKey = Preferences.Default.Get(FirstWhiteKeyKey, defaults.PortraitFirstKey),
            LandscapeFirstKey = Preferences.Default.Get(FirstWhiteKeyLandscapeKey, defaults.LandscapeFirstKey),
            Labels = Enum.IsDefined((PianoLabels)labels) ? (PianoLabels)labels : defaults.Labels,
            StripWhiteKeys = Preferences.Default.Get(StripWhiteKeysKey, defaults.StripWhiteKeys),
            StripFirstKey = Preferences.Default.Get(StripFirstWhiteKeyKey, defaults.StripFirstKey)
        };
    }

    // Returns at once; the write runs on the thread pool, newest settings winning.
    public void Save(PianoSettings settings)
    {
        lock (_gate)
        {
            _pending = settings;
        }

        Task.Run(Flush);
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_pending is { } settings)
            {
                _pending = null;
                Write(settings);
            }
        }
    }

    private static void Write(PianoSettings settings)
    {
        Preferences.Default.Set(InstrumentKey, settings.InstrumentId);
        Preferences.Default.Set(WhiteKeysPortraitKey, settings.PortraitWhiteKeys);
        Preferences.Default.Set(WhiteKeysLandscapeKey, settings.LandscapeWhiteKeys);
        Preferences.Default.Set(FirstWhiteKeyKey, settings.PortraitFirstKey);
        Preferences.Default.Set(FirstWhiteKeyLandscapeKey, settings.LandscapeFirstKey);
        Preferences.Default.Set(LabelsKey, (int)settings.Labels);
        Preferences.Default.Set(StripWhiteKeysKey, settings.StripWhiteKeys);
        Preferences.Default.Set(StripFirstWhiteKeyKey, settings.StripFirstKey);
    }
}
