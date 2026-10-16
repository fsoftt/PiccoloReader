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

    public virtual TunerStartResult Start()
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

    /// <summary>The input died mid-session: no longer running, reports silence.</summary>
    public void SimulateLoss()
    {
        IsRunning = false;
        Raise(default);
    }
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

    public int EnsureCount { get; private set; }

    /// <summary>When true, a prompt grants the permission.</summary>
    public bool GrantOnPrompt { get; set; }

    public Task<bool> EnsureGrantedAsync()
    {
        EnsureCount++;
        if (GrantOnPrompt)
        {
            Granted = true;
        }

        return Task.FromResult(Granted);
    }

    public bool IsGranted() => Granted;

    public void OpenAppSettings() => OpenSettingsCount++;
}

/// <summary>An input whose Start blocks until the test releases it, to model a pooled Start that runs late.</summary>
public sealed class GatedFakeTunerInput : FakeTunerInput
{
    private readonly ManualResetEventSlim _gate = new(false);
    private readonly ManualResetEventSlim _entered = new(false);

    public void WaitUntilStartEntered() => _entered.Wait(TimeSpan.FromSeconds(5));

    public void ReleaseStart() => _gate.Set();

    public override TunerStartResult Start()
    {
        _entered.Set();
        _gate.Wait(TimeSpan.FromSeconds(5));
        return base.Start();
    }
}
