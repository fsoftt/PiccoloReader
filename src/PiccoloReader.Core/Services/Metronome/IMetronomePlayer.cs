namespace PiccoloReader.Core.Services.Metronome;

public interface IMetronomePlayer
{
    bool IsPlaying { get; }

    /// <summary>Raised on the UI thread (beat index in the bar, accented) about when the beat is heard. Only dispatched while subscribed.</summary>
    event Action<int, bool>? BeatTicked;

    /// <summary>Raised on the UI thread whenever <see cref="IsPlaying"/> changes.</summary>
    event EventHandler? IsPlayingChanged;

    void Start();

    void Stop();

    /// <summary>Takes effect from the next beat, also while playing.</summary>
    void UpdateSettings(MetronomeSettings settings);
}
