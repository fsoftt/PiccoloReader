namespace PiccoloReader.Core.Services.Tuner;

public enum TunerStartResult
{
    Started,
    PermissionDenied,
    Failed,
    Unsupported
}

public interface ITunerInput
{
    bool IsRunning { get; }

    /// <summary>Raised on the audio thread, at most about every 50 ms. Handlers must marshal to the UI thread themselves.</summary>
    event EventHandler<TunerReading>? ReadingChanged;

    /// <summary>May block while the device opens the microphone; call it off the UI thread.</summary>
    TunerStartResult Start();

    /// <summary>Must return quickly and never wait for audio threads (it runs on the UI thread).</summary>
    void Stop();
}
