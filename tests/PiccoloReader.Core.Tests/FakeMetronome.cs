using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.Tests;

public class FakeMetronomePlayer : IMetronomePlayer
{
    public bool IsPlaying { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public List<MetronomeSettings> Updates { get; } = [];

    public int BeatTickedSubscribers => BeatTicked?.GetInvocationList().Length ?? 0;

    public event Action<int, bool>? BeatTicked;

    public event EventHandler? IsPlayingChanged;

    public void Start()
    {
        StartCount++;
        IsPlaying = true;
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        StopCount++;
        IsPlaying = false;
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSettings(MetronomeSettings settings) => Updates.Add(settings);

    public void RaiseBeat(int beatInBar, bool isAccent) => BeatTicked?.Invoke(beatInBar, isAccent);
}

public class FakeMetronomePreferenceService : IMetronomePreferenceService
{
    public MetronomeSettings Stored { get; set; } = new();

    public int SaveCount { get; private set; }

    public MetronomeSettings Load() => Stored;

    public void Save(MetronomeSettings settings)
    {
        SaveCount++;
        Stored = settings;
    }
}
