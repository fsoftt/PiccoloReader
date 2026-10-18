using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

public class MauiTunerPreferenceService : ITunerPreferenceService
{
    private const string A4Key = "TunerA4Hz";
    private const string LegacyWrittenKey = "TunerShowAsWritten";
    private const string TransposeKeyKey = "TunerTransposeKey";
    private const string OctaveShiftKey = "TunerOctaveShift";
    private const string NamingKey = "TunerNoteNaming";

    public TunerSettings Load()
    {
        var defaults = new TunerSettings();
        var naming = Preferences.Default.Get(NamingKey, (int)defaults.NoteNaming);
        var (key, octaveShift) = LoadTransposition();
        return new TunerSettings
        {
            A4Hz = Preferences.Default.Get(A4Key, defaults.A4Hz),
            Key = key,
            OctaveShift = octaveShift,
            NoteNaming = Enum.IsDefined((NoteNaming)naming) ? (NoteNaming)naming : defaults.NoteNaming
        };
    }

    // Builds before the key and octave pickers stored one on/off flag; it maps to C, octave -1.
    private static (TransposeKey Key, int OctaveShift) LoadTransposition()
    {
        if (!Preferences.Default.ContainsKey(TransposeKeyKey) && !Preferences.Default.ContainsKey(OctaveShiftKey))
        {
            var legacy = Preferences.Default.ContainsKey(LegacyWrittenKey) && Preferences.Default.Get(LegacyWrittenKey, false);
            return TunerSettings.FromLegacyWritten(legacy);
        }

        var key = Preferences.Default.Get(TransposeKeyKey, (int)TransposeKey.C);
        return (
            Enum.IsDefined((TransposeKey)key) ? (TransposeKey)key : TransposeKey.C,
            Preferences.Default.Get(OctaveShiftKey, 0));
    }

    public void Save(TunerSettings settings)
    {
        Preferences.Default.Set(A4Key, settings.A4Hz);
        Preferences.Default.Set(TransposeKeyKey, (int)settings.Key);
        Preferences.Default.Set(OctaveShiftKey, settings.OctaveShift);
        Preferences.Default.Remove(LegacyWrittenKey);
        Preferences.Default.Set(NamingKey, (int)settings.NoteNaming);
    }
}
