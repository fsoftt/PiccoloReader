using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Services;

// Platforms without a metronome backend (iOS): never plays.
public class UnsupportedMetronomePlayer : IMetronomePlayer
{
    public bool IsPlaying => false;

    public event Action<int, bool>? BeatTicked
    {
        add { }
        remove { }
    }

    public event EventHandler? IsPlayingChanged
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void UpdateSettings(MetronomeSettings settings)
    {
    }
}
