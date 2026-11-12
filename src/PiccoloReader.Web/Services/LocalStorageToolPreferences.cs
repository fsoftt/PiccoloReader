using System.Globalization;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Web.Services;

// Metronome, piano and tuner preferences over localStorage (counterparts of the Maui* classes).
internal static class Store
{
    public static int Int(BrowserInterop b, string key, int fallback) =>
        int.TryParse(b.GetLocalStorageSync(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static void Set(BrowserInterop b, string key, int value) =>
        b.SetLocalStorageSync(key, value.ToString(CultureInfo.InvariantCulture));

    public static TEnum Enum<TEnum>(BrowserInterop b, string key, TEnum fallback) where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(b.GetLocalStorageSync(key), out var v) && System.Enum.IsDefined(v) ? v : fallback;
}

public sealed class LocalStorageMetronomePreferenceService(BrowserInterop b) : IMetronomePreferenceService
{
    public MetronomeSettings Load()
    {
        var d = new MetronomeSettings();
        return new MetronomeSettings
        {
            Bpm = Store.Int(b, "piccolo.metronome.bpm", d.Bpm),
            BeatsPerBar = Store.Int(b, "piccolo.metronome.beats", d.BeatsPerBar),
            Subdivision = Store.Int(b, "piccolo.metronome.subdivision", d.Subdivision),
            AccentFirstBeat = b.GetLocalStorageSync("piccolo.metronome.accent") != "0"
        };
    }

    public void Save(MetronomeSettings s)
    {
        Store.Set(b, "piccolo.metronome.bpm", s.Bpm);
        Store.Set(b, "piccolo.metronome.beats", s.BeatsPerBar);
        Store.Set(b, "piccolo.metronome.subdivision", s.Subdivision);
        b.SetLocalStorageSync("piccolo.metronome.accent", s.AccentFirstBeat ? "1" : "0");
    }
}

public sealed class LocalStoragePianoPreferenceService(BrowserInterop b) : IPianoPreferenceService
{
    public PianoSettings Load()
    {
        var d = new PianoSettings();
        return new PianoSettings
        {
            InstrumentId = b.GetLocalStorageSync("piccolo.piano.instrument") ?? d.InstrumentId,
            PortraitWhiteKeys = Store.Int(b, "piccolo.piano.whiteKeysPortrait", d.PortraitWhiteKeys),
            LandscapeWhiteKeys = Store.Int(b, "piccolo.piano.whiteKeysLandscape", d.LandscapeWhiteKeys),
            PortraitFirstKey = Store.Int(b, "piccolo.piano.firstKeyPortrait", d.PortraitFirstKey),
            LandscapeFirstKey = Store.Int(b, "piccolo.piano.firstKeyLandscape", d.LandscapeFirstKey),
            Labels = Store.Enum(b, "piccolo.piano.labels", d.Labels)
        };
    }

    public void Save(PianoSettings s)
    {
        b.SetLocalStorageSync("piccolo.piano.instrument", s.InstrumentId);
        Store.Set(b, "piccolo.piano.whiteKeysPortrait", s.PortraitWhiteKeys);
        Store.Set(b, "piccolo.piano.whiteKeysLandscape", s.LandscapeWhiteKeys);
        Store.Set(b, "piccolo.piano.firstKeyPortrait", s.PortraitFirstKey);
        Store.Set(b, "piccolo.piano.firstKeyLandscape", s.LandscapeFirstKey);
        b.SetLocalStorageSync("piccolo.piano.labels", s.Labels.ToString());
    }
}

public sealed class LocalStorageTunerPreferenceService(BrowserInterop b) : ITunerPreferenceService
{
    public TunerSettings Load() => new()
    {
        A4Hz = Store.Int(b, "piccolo.tuner.a4", TunerSettings.DefaultA4Hz),
        Key = Store.Enum(b, "piccolo.tuner.key", TransposeKey.C),
        OctaveShift = Store.Int(b, "piccolo.tuner.octave", 0),
        NoteNaming = Store.Enum(b, "piccolo.tuner.naming", NoteNaming.Auto)
    };

    public void Save(TunerSettings s)
    {
        Store.Set(b, "piccolo.tuner.a4", s.A4Hz);
        b.SetLocalStorageSync("piccolo.tuner.key", s.Key.ToString());
        Store.Set(b, "piccolo.tuner.octave", s.OctaveShift);
        b.SetLocalStorageSync("piccolo.tuner.naming", s.NoteNaming.ToString());
    }
}

public sealed class LocalStorageTunerStripPreferenceService(BrowserInterop b) : ITunerStripPreferenceService
{
    public bool IsEnabled
    {
        get => b.GetLocalStorageSync("piccolo.tunerStrip.enabled") == "1";
        set => b.SetLocalStorageSync("piccolo.tunerStrip.enabled", value ? "1" : "0");
    }

    public TunerStripPosition Position
    {
        get => Store.Enum(b, "piccolo.tunerStrip.position", TunerStripPosition.Right);
        set => b.SetLocalStorageSync("piccolo.tunerStrip.position", value.ToString());
    }
}

// First-run tutorial (TutorialViewModel) and the concert mode coach marks, over localStorage.
public sealed class LocalStorageTutorialPreferenceService(BrowserInterop b) : ITutorialPreferenceService
{
    public bool GetTutorialCompleted() => b.GetLocalStorageSync("piccolo.tutorialCompleted") == "1";

    public void SetTutorialCompleted(bool completed) => b.SetLocalStorageSync("piccolo.tutorialCompleted", completed ? "1" : "0");
}

public sealed class LocalStorageConcertTutorialPreferenceService(BrowserInterop b) : IConcertTutorialPreferenceService
{
    public bool LibrarySeen
    {
        get => b.GetLocalStorageSync("piccolo.concertTutorial.library") == "1";
        set => b.SetLocalStorageSync("piccolo.concertTutorial.library", value ? "1" : "0");
    }

    public bool ViewerSeen
    {
        get => b.GetLocalStorageSync("piccolo.concertTutorial.viewer") == "1";
        set => b.SetLocalStorageSync("piccolo.concertTutorial.viewer", value ? "1" : "0");
    }

    public void Reset()
    {
        LibrarySeen = false;
        ViewerSeen = false;
    }
}
