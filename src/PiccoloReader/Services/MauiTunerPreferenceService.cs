using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

public class MauiTunerPreferenceService : ITunerPreferenceService
{
    private const string A4Key = "TunerA4Hz";
    private const string WrittenKey = "TunerShowAsWritten";
    private const string NamingKey = "TunerNoteNaming";

    public TunerSettings Load()
    {
        var defaults = new TunerSettings();
        var naming = Preferences.Default.Get(NamingKey, (int)defaults.NoteNaming);
        return new TunerSettings
        {
            A4Hz = Preferences.Default.Get(A4Key, defaults.A4Hz),
            ShowAsWritten = Preferences.Default.Get(WrittenKey, defaults.ShowAsWritten),
            NoteNaming = Enum.IsDefined((NoteNaming)naming) ? (NoteNaming)naming : defaults.NoteNaming
        };
    }

    public void Save(TunerSettings settings)
    {
        Preferences.Default.Set(A4Key, settings.A4Hz);
        Preferences.Default.Set(WrittenKey, settings.ShowAsWritten);
        Preferences.Default.Set(NamingKey, (int)settings.NoteNaming);
    }
}
