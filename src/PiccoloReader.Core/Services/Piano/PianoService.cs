using MeltySynth;

namespace PiccoloReader.Core.Services.Piano;

public enum PianoLoadState
{
    NotLoaded,
    Loading,
    Ready,
    Failed
}

/// <summary>
/// The piano as the screens see it. Screens hold a lease (<see cref="Acquire"/>) while visible;
/// every note call is a cheap enqueue and a no-op while the sounds are not ready.
/// </summary>
public interface IPianoService
{
    PianoLoadState State { get; }

    /// <summary>Raised on a background thread whenever <see cref="State"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Takes a lease: loads the sounds on first use and keeps the stream running. Never blocks.</summary>
    void Acquire();

    /// <summary>Gives a lease back. The stream stops a moment after the last one.</summary>
    void Release();

    void NoteOn(int note, int velocity);

    void NoteOff(int note);

    void SetSustain(bool on);

    void SetInstrument(PianoInstrument instrument);

    /// <summary>Silences every note and lifts the pedal.</summary>
    void Silence();

    /// <summary>The app went to the background: silence and stop the stream, whatever the leases.</summary>
    void Suspend();

    /// <summary>The app is back: restart the stream if a screen still holds a lease.</summary>
    void Resume();
}

/// <summary>
/// Owns the SoundFont, the <see cref="PianoSynthEngine"/> and the audio output. Costs nothing
/// until the first <see cref="Acquire"/>: no SoundFont, no track, no thread. The SoundFont is
/// loaded once and kept; the stream starts with the first lease and stops
/// <see cref="IdleStopDelay"/> after the last release, so moving between the tab and the viewer
/// strip does not rebuild the track. All heavy work runs on the thread pool.
/// </summary>
public sealed class PianoService : IPianoService
{
    public static readonly TimeSpan IdleStopDelay = TimeSpan.FromSeconds(2);

    private readonly IPianoAudioOutput _output;
    private readonly Func<Task<Stream>> _openSoundFont;
    private readonly TimeSpan _idleDelay;
    private readonly object _gate = new();
    private readonly object _outputGate = new();
    private PianoSynthEngine? _engine;
    private PianoLoadState _state = PianoLoadState.NotLoaded;
    private PianoInstrument _instrument = PianoInstruments.Default;
    private int _leases;
    private bool _suspended;
    private bool _idleExpired;
    private int _idleVersion;

    /// <param name="openSoundFont">Opens the SoundFont stream; called on a pool thread, disposed after the load.</param>
    public PianoService(IPianoAudioOutput output, Func<Task<Stream>> openSoundFont, TimeSpan? idleDelay = null)
    {
        _output = output;
        _openSoundFont = openSoundFont;
        _idleDelay = idleDelay ?? IdleStopDelay;
    }

    public event EventHandler? StateChanged;

    public PianoLoadState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public void Acquire()
    {
        var load = false;
        lock (_gate)
        {
            _leases++;
            _idleExpired = false;
            _idleVersion++;
            if (_state is PianoLoadState.NotLoaded or PianoLoadState.Failed)
            {
                _state = PianoLoadState.Loading;
                load = true;
            }
        }

        if (load)
        {
            RaiseStateChanged();
            Task.Run(LoadAsync);
        }
        else
        {
            Task.Run(Reconcile);
        }
    }

    public void Release()
    {
        int version;
        lock (_gate)
        {
            if (_leases == 0)
            {
                return;
            }

            _leases--;
            if (_leases > 0)
            {
                return;
            }

            version = ++_idleVersion;
        }

        Task.Run(() => StopWhenIdleAsync(version));
    }

    public void NoteOn(int note, int velocity) => Engine?.NoteOn(note, velocity);

    public void NoteOff(int note) => Engine?.NoteOff(note);

    public void SetSustain(bool on) => Engine?.SetSustain(on);

    public void SetInstrument(PianoInstrument instrument)
    {
        PianoSynthEngine? engine;
        lock (_gate)
        {
            _instrument = instrument;
            engine = _engine;
        }

        engine?.SetInstrument(instrument);
    }

    public void Silence() => Engine?.Silence();

    public void Suspend()
    {
        lock (_gate)
        {
            _suspended = true;
        }

        Engine?.Silence();
        Task.Run(Reconcile);
    }

    public void Resume()
    {
        lock (_gate)
        {
            _suspended = false;
        }

        Task.Run(Reconcile);
    }

    private PianoSynthEngine? Engine
    {
        get
        {
            lock (_gate)
            {
                return _engine;
            }
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            SoundFont soundFont;
            using (var source = await _openSoundFont().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                await source.CopyToAsync(buffer).ConfigureAwait(false);
                buffer.Position = 0;
                soundFont = new SoundFont(buffer);
            }

            PianoInstrument instrument;
            lock (_gate)
            {
                instrument = _instrument;
            }

            var engine = new PianoSynthEngine(soundFont, _output.PreferredSampleRate, instrument);
            lock (_gate)
            {
                _engine = engine;
                _state = PianoLoadState.Ready;

                // A choice made while the sounds were loading wins over the one the engine started with.
                if (_instrument != instrument)
                {
                    engine.SetInstrument(_instrument);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Piano sounds failed to load: {ex.Message}");
            lock (_gate)
            {
                _state = PianoLoadState.Failed;
            }
        }

        RaiseStateChanged();
        Reconcile();
    }

    private async Task StopWhenIdleAsync(int version)
    {
        try
        {
            await Task.Delay(_idleDelay).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        lock (_gate)
        {
            if (version != _idleVersion || _leases > 0)
            {
                return;
            }

            _idleExpired = true;
        }

        Reconcile();
    }

    // Brings the stream in line with the leases; idempotent, so overlapping calls are harmless.
    private void Reconcile()
    {
        try
        {
            lock (_outputGate)
            {
                PianoSynthEngine? engine;
                bool wanted;
                lock (_gate)
                {
                    engine = _engine;
                    wanted = engine is not null && !_suspended && (_leases > 0 || !_idleExpired);
                }

                if (wanted && !_output.IsRunning)
                {
                    // Notes queued while stopped must not sound when the stream comes back.
                    engine!.Silence();
                    _output.Start(engine);
                }
                else if (!wanted && _output.IsRunning)
                {
                    engine?.Silence();
                    _output.Stop();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Piano stream change failed: {ex.Message}");
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
