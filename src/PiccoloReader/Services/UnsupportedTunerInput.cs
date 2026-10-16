using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

// Platforms without a tuner backend (iOS): never listens.
public class UnsupportedTunerInput : ITunerInput
{
    public bool IsRunning => false;

    public event EventHandler<TunerReading>? ReadingChanged
    {
        add { }
        remove { }
    }

    public TunerStartResult Start() => TunerStartResult.Unsupported;

    public void Stop()
    {
    }
}
