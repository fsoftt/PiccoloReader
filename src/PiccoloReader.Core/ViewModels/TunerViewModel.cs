using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.ViewModels;

public enum TunerState
{
    Idle,
    NoPermission,

    /// <summary>Input running and a pitch is being shown.</summary>
    Listening,

    /// <summary>Input running but no pitch is being played.</summary>
    Silent,
    Unsupported,
    Error
}

/// <summary>
/// Tuner screen state. The metronome is deliberately left alone: it keeps
/// playing while the tuner listens.
/// </summary>
public partial class TunerViewModel : ObservableObject
{
    private readonly ITunerInput _input;
    private readonly ITunerPreferenceService _preferences;
    private readonly IMicrophonePermissionService _permission;
    private readonly IAdsPreferenceService _adsPreferenceService;
    private readonly Action<Action> _post;
    private readonly Action _applyLatest;
    private readonly object _latestLock = new();
    private TunerReading _latest;
    private bool _applyPending;
    private bool _isDirty;
    private bool _attached;
    private int _attachVersion;
    private float _lastHz;
    private TunerSettings _settings;
    private NoteNaming _resolvedNaming;

    [ObservableProperty]
    private TunerState _state = TunerState.Idle;

    [ObservableProperty]
    private string _noteName = string.Empty;

    [ObservableProperty]
    private int _octave;

    [ObservableProperty]
    private float _cents;

    [ObservableProperty]
    private float _frequencyHz;

    [ObservableProperty]
    private bool _isInTune;

    [ObservableProperty]
    private bool _showAdsBanner;

    [ObservableProperty]
    private int _a4Hz;

    [ObservableProperty]
    private TransposeKey _transposeKey;

    [ObservableProperty]
    private int _octaveShift;

    [ObservableProperty]
    private NoteNaming _noteNaming;

    /// <param name="post">
    /// Runs an action on the UI thread. The input raises readings on the audio
    /// thread; the default runs inline, which suits tests. It must be
    /// fire-and-forget and never invoke-and-wait: Detach asks the input to stop
    /// without waiting, but blocking on the UI thread could deadlock.
    /// </param>
    public TunerViewModel(
        ITunerInput input,
        ITunerPreferenceService preferences,
        IMicrophonePermissionService permission,
        IAdsPreferenceService adsPreferenceService,
        Action<Action>? post = null)
    {
        _input = input;
        _preferences = preferences;
        _permission = permission;
        _adsPreferenceService = adsPreferenceService;
        _post = post ?? (static action => action());
        _applyLatest = ApplyLatest;

        var settings = preferences.Load();
        _a4Hz = settings.A4Hz;
        _transposeKey = settings.Key;
        _octaveShift = settings.OctaveShift;
        _noteNaming = settings.NoteNaming;
        _settings = settings;
        _resolvedNaming = NoteNames.Resolve(settings.NoteNaming, CultureInfo.CurrentUICulture);
    }

    /// <summary>Current settings snapshot; rebuilt only when a setting changes.</summary>
    public TunerSettings Settings => _settings;

    /// <summary>Starts listening. Call when the tuner UI becomes visible.</summary>
    public async Task AttachAsync()
    {
        var version = ++_attachVersion;
        _attached = true;
        _resolvedNaming = NoteNames.Resolve(NoteNaming, CultureInfo.CurrentUICulture);
        lock (_latestLock)
        {
            _applyPending = false;
            _latest = default;
        }

        ShowAdsBanner = _adsPreferenceService.GetSupportWithAdsEnabled();

        var granted = await _permission.EnsureGrantedAsync();
        if (version != _attachVersion || !_attached)
        {
            return;
        }

        if (!granted)
        {
            State = TunerState.NoPermission;
            return;
        }

        _input.ReadingChanged -= OnReadingChanged;
        _input.ReadingChanged += OnReadingChanged;
        var result = await Task.Run(_input.Start);
        if (version != _attachVersion || !_attached)
        {
            // Detached (or re-attached) while the microphone was opening. Detach may
            // have stopped before this late Start opened it, so when nothing newer
            // owns the input, give it back.
            if (!_attached)
            {
                _input.Stop();
            }

            return;
        }

        if (result != TunerStartResult.Started)
        {
            _input.ReadingChanged -= OnReadingChanged;
            State = result switch
            {
                TunerStartResult.PermissionDenied => TunerState.NoPermission,
                TunerStartResult.Unsupported => TunerState.Unsupported,
                _ => TunerState.Error
            };
            return;
        }

        ClearNote();
        State = TunerState.Silent;
    }

    /// <summary>Stops listening so no audio or UI work happens while hidden.</summary>
    public void Detach()
    {
        _attached = false;
        _attachVersion++;
        _input.ReadingChanged -= OnReadingChanged;
        _input.Stop();
        State = TunerState.Idle;
        PersistIfDirty();
    }

    public void PersistIfDirty()
    {
        if (!_isDirty)
        {
            return;
        }

        try
        {
            _preferences.Save(Settings);
            _isDirty = false;
        }
        catch (Exception)
        {
            // A failed write must not break the UI; it stays dirty and the next
            // change or Detach retries.
        }
    }

    [RelayCommand]
    private void IncreaseA4()
    {
        if (A4Hz < TunerSettings.MaxA4Hz)
        {
            A4Hz++;
        }
    }

    [RelayCommand]
    private void DecreaseA4()
    {
        if (A4Hz > TunerSettings.MinA4Hz)
        {
            A4Hz--;
        }
    }

    [RelayCommand]
    private void SetA4Preset(int hz) => A4Hz = hz;

    [RelayCommand]
    private void OpenSettings() => _permission.OpenAppSettings();

    partial void OnA4HzChanged(int value)
    {
        var clamped = Math.Clamp(value, TunerSettings.MinA4Hz, TunerSettings.MaxA4Hz);
        if (clamped != value)
        {
            A4Hz = clamped;
            return;
        }

        SettingsChanged();
    }

    partial void OnTransposeKeyChanged(TransposeKey value) => SettingsChanged();

    partial void OnOctaveShiftChanged(int value)
    {
        var clamped = Math.Clamp(value, TunerSettings.MinOctaveShift, TunerSettings.MaxOctaveShift);
        if (clamped != value)
        {
            OctaveShift = clamped;
            return;
        }

        SettingsChanged();
    }

    partial void OnNoteNamingChanged(NoteNaming value)
    {
        _resolvedNaming = NoteNames.Resolve(value, CultureInfo.CurrentUICulture);
        SettingsChanged();
    }

    private void SettingsChanged()
    {
        var snapshot = new TunerSettings
        {
            A4Hz = A4Hz,
            Key = TransposeKey,
            OctaveShift = OctaveShift,
            NoteNaming = NoteNaming
        };
        if (snapshot == _settings)
        {
            return;
        }

        _settings = snapshot;
        _isDirty = true;
        PersistIfDirty();
        if (_lastHz > 0f)
        {
            ShowNote(_lastHz);
            if (State == TunerState.Silent)
            {
                HoldNote();
            }
        }
    }

    // Audio thread. Keeps only the newest reading and posts at most one pending
    // UI update, so a slow UI thread never queues work and nothing is allocated.
    private void OnReadingChanged(object? sender, TunerReading reading)
    {
        lock (_latestLock)
        {
            _latest = reading;
            if (_applyPending)
            {
                return;
            }

            _applyPending = true;
        }

        _post(_applyLatest);
    }

    private void ApplyLatest()
    {
        TunerReading reading;
        lock (_latestLock)
        {
            reading = _latest;
            _applyPending = false;
        }

        // A reading already in flight when listening stopped must not revive the UI.
        if (!_attached || State is TunerState.Idle or TunerState.NoPermission or TunerState.Unsupported or TunerState.Error)
        {
            return;
        }

        if (!reading.HasPitch)
        {
            // Keep the last note (and _lastHz, so a settings change can still
            // rename it) on screen until a new note sounds. State flips first so
            // the view never sees Listening with zeroed values.
            State = TunerState.Silent;
            HoldNote();
            return;
        }

        _lastHz = reading.Hz;
        ShowNote(reading.Hz);
        State = TunerState.Listening;
    }

    private void ShowNote(float hz)
    {
        var settings = _settings;
        var note = NoteMapper.FromHz(hz, settings.A4Hz, settings.TransposeSemitones);
        var naming = _resolvedNaming;
        NoteName = NoteNames.Get(note.PitchClass, naming);
        Octave = note.Octave;
        Cents = MathF.Round(note.Cents, 1);
        FrequencyHz = MathF.Round(hz, 1);
        IsInTune = MathF.Abs(Cents) <= 5f;
    }

    // Silence: keep the note name and octave, drop the live values.
    private void HoldNote()
    {
        Cents = 0f;
        FrequencyHz = 0f;
        IsInTune = false;
    }

    private void ClearNote()
    {
        _lastHz = 0f;
        NoteName = string.Empty;
        Octave = 0;
        Cents = 0f;
        FrequencyHz = 0f;
        IsInTune = false;
    }
}
