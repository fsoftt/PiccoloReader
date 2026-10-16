namespace PiccoloReader.Core.Services.Tuner;

public enum TunerStripBucket
{
    InTune,
    Close,
    Far
}

/// <summary>Applies strip values to the native view. Always called on the UI thread; must only move the marker and recolor it.</summary>
public interface ITunerStripView
{
    void Apply(bool visible, int offsetPx, TunerStripBucket bucket);
}

/// <summary>
/// Drives the thin tuner strip of the sheet viewer. Readings arrive on the audio
/// thread and are reduced to (visible, whole-pixel offset, color bucket); the UI
/// is only touched when that state changes, with at most one post in flight and
/// nothing allocated per reading.
/// </summary>
public sealed class TunerStripController
{
    public const float RangeCents = 50f;
    public const float InTuneCents = 5f;
    public const float CloseCents = 15f;

    private readonly record struct StripState(bool Visible, int OffsetPx, TunerStripBucket Bucket);

    private readonly ITunerInput _input;
    private readonly ITunerPreferenceService _tunerPreferences;
    private readonly ITunerStripPreferenceService _stripPreferences;
    private readonly IMicrophonePermissionService _permission;
    private readonly Action<Action> _post;
    private readonly Action _applyLatest;
    private readonly EventHandler<TunerReading> _onReading;
    private readonly object _latestLock = new();
    private readonly Action _onInputLost;

    private ITunerStripView? _view;
    private StripState _target;
    private bool _applyPending;
    private volatile int _a4Hz = TunerSettings.DefaultA4Hz;
    private volatile float _travelPx;
    private int _attachVersion;
    private bool _subscribed;
    private bool _visible = true;
    private bool _toggling;
    private volatile bool _running;
    private int _lostPending;

    public TunerStripController(
        ITunerInput input,
        ITunerPreferenceService tunerPreferences,
        ITunerStripPreferenceService stripPreferences,
        IMicrophonePermissionService permission,
        Action<Action> post)
    {
        _input = input;
        _tunerPreferences = tunerPreferences;
        _stripPreferences = stripPreferences;
        _permission = permission;
        _post = post;
        _applyLatest = ApplyLatest;
        _onReading = OnReadingChanged;
        _onInputLost = HandleInputLost;
    }

    /// <summary>True while the strip is on and listening (or opening the microphone). Drives the toggle button and the strip's presence.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Raised on the UI thread when the strip turned itself off (the input failed or died), so the toggle button can refresh.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Connects the native view (UI thread). Null disconnects it.</summary>
    public void Bind(ITunerStripView? view) => _view = view;

    /// <summary>Distance in pixels the marker travels from the left end to the right end of the track.</summary>
    public void SetTravel(float travelPx) => _travelPx = Math.Max(0f, travelPx);

    /// <summary>
    /// Starts listening when the preference is on and the microphone permission is
    /// already granted (never prompts). Call when the viewer is visible; UI thread.
    /// A no-op while already listening.
    /// </summary>
    public async Task AttachAsync()
    {
        _visible = true;
        if (IsOn && _subscribed)
        {
            return;
        }

        var version = ++_attachVersion;
        Unsubscribe();
        if (!_stripPreferences.IsEnabled || !_permission.IsGranted())
        {
            IsOn = false;
            return;
        }

        IsOn = true;
        _a4Hz = _tunerPreferences.Load().A4Hz;
        ResetTarget();
        _input.ReadingChanged += _onReading;
        _subscribed = true;

        var result = await Task.Run(_input.Start);
        if (version != _attachVersion)
        {
            // Detached (or re-attached) while the microphone was opening. A late
            // Start may have opened it after Detach's Stop found nothing to stop;
            // when nothing newer owns the input, give it back.
            if (!IsOn)
            {
                _input.Stop();
            }

            return;
        }

        if (result != TunerStartResult.Started)
        {
            Unsubscribe();
            IsOn = false;
            _view?.Apply(false, 0, TunerStripBucket.Far);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _running = true;
    }

    /// <summary>
    /// Stops listening. Only stops the shared input when this strip started it, so
    /// a hidden viewer never kills a tuner page that took the microphone.
    /// </summary>
    public void Detach()
    {
        _visible = false;
        _attachVersion++;
        var wasOn = IsOn;
        IsOn = false;
        _running = false;
        Unsubscribe();
        if (wasOn)
        {
            _input.Stop();
        }

        ResetTarget();
        _view?.Apply(false, 0, TunerStripBucket.Far);
    }

    /// <summary>
    /// Toggle button tap. Turning on asks for the microphone permission here (and only
    /// here); a denial leaves the strip and the preference off. Returns the new state.
    /// A tap while a previous one is still running is ignored.
    /// </summary>
    public async Task<bool> ToggleAsync()
    {
        if (_toggling)
        {
            return IsOn;
        }

        _toggling = true;
        try
        {
            if (IsOn)
            {
                _stripPreferences.IsEnabled = false;
                Detach();
                _visible = true;
                return false;
            }

            if (!await _permission.EnsureGrantedAsync())
            {
                _stripPreferences.IsEnabled = false;
                return false;
            }

            _stripPreferences.IsEnabled = true;

            // The viewer was hidden while the prompt was open: the next attach starts it.
            if (!_visible)
            {
                return false;
            }

            await AttachAsync();
            return IsOn;
        }
        finally
        {
            _toggling = false;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            _input.ReadingChanged -= _onReading;
            _subscribed = false;
        }
    }

    private void ResetTarget()
    {
        lock (_latestLock)
        {
            _target = default;
        }
    }

    /// <summary>Maps a reading to the strip state. Pure; no allocation.</summary>
    internal static void Map(in TunerReading reading, int a4Hz, float travelPx, out bool visible, out int offsetPx, out TunerStripBucket bucket)
    {
        if (!reading.HasPitch || reading.Hz <= 0f)
        {
            visible = false;
            offsetPx = 0;
            bucket = TunerStripBucket.Far;
            return;
        }

        var cents = Math.Clamp(NoteMapper.FromHz(reading.Hz, a4Hz).Cents, -RangeCents, RangeCents);
        var abs = MathF.Abs(cents);
        visible = true;
        offsetPx = (int)MathF.Round((cents + RangeCents) / (2f * RangeCents) * travelPx);
        bucket = abs <= InTuneCents ? TunerStripBucket.InTune
            : abs <= CloseCents ? TunerStripBucket.Close
            : TunerStripBucket.Far;
    }

    // Audio thread. Keeps only the newest state and posts at most one pending UI
    // update; nothing is posted while the visible state is unchanged.
    private void OnReadingChanged(object? sender, TunerReading reading)
    {
        // The input dropped out (another app took the microphone): it reports silence and
        // is no longer running. Turn the strip off once, on the UI thread.
        if (!reading.HasPitch && _running && !_input.IsRunning && Interlocked.Exchange(ref _lostPending, 1) == 0)
        {
            _post(_onInputLost);
            return;
        }

        Map(reading, _a4Hz, _travelPx, out var visible, out var offsetPx, out var bucket);
        var state = new StripState(visible, offsetPx, bucket);
        lock (_latestLock)
        {
            if (state == _target)
            {
                return;
            }

            _target = state;
            if (_applyPending)
            {
                return;
            }

            _applyPending = true;
        }

        _post(_applyLatest);
    }

    private void HandleInputLost()
    {
        _lostPending = 0;
        if (!IsOn || !_running)
        {
            return;
        }

        // The preference stays as it is: the next attach tries again.
        IsOn = false;
        _running = false;
        Unsubscribe();
        _input.Stop();
        ResetTarget();
        _view?.Apply(false, 0, TunerStripBucket.Far);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyLatest()
    {
        StripState state;
        lock (_latestLock)
        {
            state = _target;
            _applyPending = false;
        }

        // A reading already in flight when listening stopped must not revive the strip.
        if (!IsOn)
        {
            return;
        }

        _view?.Apply(state.Visible, state.OffsetPx, state.Bucket);
    }
}
