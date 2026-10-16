using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests;

public class FakeTunerInput : ITunerInput
{
    public bool IsRunning { get; private set; }

    public TunerStartResult StartResult { get; set; } = TunerStartResult.Started;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int SubscriberCount => ReadingChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<TunerReading>? ReadingChanged;

    public TunerStartResult Start()
    {
        StartCount++;
        IsRunning = StartResult == TunerStartResult.Started;
        return StartResult;
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
    }

    public void Raise(TunerReading reading) => ReadingChanged?.Invoke(this, reading);
}

public class FakeTunerPreferenceService : ITunerPreferenceService
{
    public TunerSettings Stored { get; set; } = new();

    public int SaveCount { get; private set; }

    public TunerSettings Load() => Stored;

    public void Save(TunerSettings settings)
    {
        SaveCount++;
        Stored = settings;
    }
}

public class FakeMicrophonePermissionService : IMicrophonePermissionService
{
    public bool Granted { get; set; } = true;

    public int OpenSettingsCount { get; private set; }

    public Task<bool> EnsureGrantedAsync() => Task.FromResult(Granted);

    public void OpenAppSettings() => OpenSettingsCount++;
}
