using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Services;

/// <summary>Platforms without piano audio (iOS for now): the tab is hidden, so this is never started.</summary>
public class UnsupportedPianoAudioOutput : IPianoAudioOutput
{
    public int PreferredSampleRate => 48000;

    public bool IsRunning => false;

    public event EventHandler? Died { add { } remove { } }

    public bool Start(PianoSynthEngine engine) => false;

    public void Stop()
    {
    }
}
