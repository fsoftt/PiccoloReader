using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Services;

public class MauiMetronomePreferenceService : IMetronomePreferenceService
{
    private const string BpmKey = "MetronomeBpm";
    private const string BeatsPerBarKey = "MetronomeBeatsPerBar";
    private const string AccentKey = "MetronomeAccentFirstBeat";
    private const string SubdivisionKey = "MetronomeSubdivision";

    public MetronomeSettings Load()
    {
        var defaults = new MetronomeSettings();
        return new MetronomeSettings
        {
            Bpm = Preferences.Default.Get(BpmKey, defaults.Bpm),
            BeatsPerBar = Preferences.Default.Get(BeatsPerBarKey, defaults.BeatsPerBar),
            AccentFirstBeat = Preferences.Default.Get(AccentKey, defaults.AccentFirstBeat),
            Subdivision = Preferences.Default.Get(SubdivisionKey, defaults.Subdivision)
        };
    }

    public void Save(MetronomeSettings settings)
    {
        Preferences.Default.Set(BpmKey, settings.Bpm);
        Preferences.Default.Set(BeatsPerBarKey, settings.BeatsPerBar);
        Preferences.Default.Set(AccentKey, settings.AccentFirstBeat);
        Preferences.Default.Set(SubdivisionKey, settings.Subdivision);
    }
}
