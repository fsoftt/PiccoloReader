using MeltySynth;
using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests;

/// <summary>The real trimmed SoundFont from the app assets, parsed once for all piano tests.</summary>
public static class PianoAsset
{
    private static readonly Lazy<SoundFont> Font = new(() => new SoundFont(Path!), true);

    public static string Path { get; } = FindPath();

    public static SoundFont SoundFont => Font.Value;

    public static Task<Stream> OpenAsync() => Task.FromResult<Stream>(File.OpenRead(Path));

    private static string FindPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "src", "PiccoloReader", "Resources", "Raw", "piano.sf2");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("piano.sf2 not found above the test output folder.");
    }
}

public class FakePianoPreferenceService : IPianoPreferenceService
{
    public PianoSettings Stored { get; set; } = new();

    public int SaveCount { get; private set; }

    public PianoSettings Load() => Stored;

    public void Save(PianoSettings settings)
    {
        SaveCount++;
        Stored = settings;
    }
}

public class FakePianoAudioOutput : IPianoAudioOutput
{
    public int PreferredSampleRate => 48000;

    public bool IsRunning { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public bool Start(PianoSynthEngine engine)
    {
        StartCount++;
        IsRunning = true;
        return true;
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
    }
}

public class FakePianoService : IPianoService
{
    public PianoLoadState State { get; set; } = PianoLoadState.Ready;

    public int Leases { get; private set; }

    public List<int> NotesOn { get; } = [];

    public List<int> NotesOff { get; } = [];

    public bool Sustain { get; private set; }

    public PianoInstrument? Instrument { get; private set; }

    public event EventHandler? StateChanged;

    public void Acquire() => Leases++;

    public void Release() => Leases--;

    public void NoteOn(int note, int velocity) => NotesOn.Add(note);

    public void NoteOff(int note) => NotesOff.Add(note);

    public void SetSustain(bool on) => Sustain = on;

    public void SetInstrument(PianoInstrument instrument) => Instrument = instrument;

    public void Silence()
    {
    }

    public void Suspend()
    {
    }

    public void Resume()
    {
    }

    public void RaiseStateChanged(PianoLoadState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
