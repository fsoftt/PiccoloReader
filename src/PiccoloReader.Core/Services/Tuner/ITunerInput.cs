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

    TunerStartResult Start();

    void Stop();
}
