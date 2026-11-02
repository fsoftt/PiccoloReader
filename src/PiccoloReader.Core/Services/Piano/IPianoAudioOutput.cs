namespace PiccoloReader.Core.Services.Piano;

/// <summary>
/// Platform audio sink that pulls blocks from a <see cref="PianoSynthEngine"/> on its own
/// thread. <see cref="Start"/> and <see cref="Stop"/> may take a few milliseconds: call them
/// off the UI thread.
/// </summary>
public interface IPianoAudioOutput
{
    /// <summary>Native output rate; the engine renders at this rate to avoid a system resampler.</summary>
    int PreferredSampleRate { get; }

    bool IsRunning { get; }

    /// <summary>Opens the stream and starts pulling audio. Returns false when no stream could be opened.</summary>
    bool Start(PianoSynthEngine engine);

    /// <summary>Stops the stream and frees the thread and the track. Safe when not running.</summary>
    void Stop();
}
