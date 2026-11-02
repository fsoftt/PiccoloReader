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

    /// <summary>How long after the stream stopped the SoundFont and engine are dropped (about 30 MB).</summary>
    public static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromSeconds(60);

    /// <summary>Minimum gap between two automatic stream restarts (dead audio thread, note on a stopped stream).</summary>
    private const long RestartThrottleMilliseconds = 1000;

    // piano.sf2 is 7.87 MB; sizing the copy buffer above it avoids the doubling reallocations.
    private const int ExpectedAssetBytes = 8 * 1024 * 1024;

    private readonly IPianoAudioOutput _output;
    private readonly Func<Task<Stream>> _openSoundFont;
    private readonly TimeSpan _idleDelay;
    private readonly TimeSpan _unloadDelay;
    private readonly object _gate = new();
    private readonly object _outputGate = new();
    private PianoSynthEngine? _engine;
    private PianoLoadState _state = PianoLoadState.NotLoaded;
    private PianoInstrument _instrument = PianoInstruments.Default;
    private int _leases;
    private bool _sustain;
    private long _lastRestartTick = long.MinValue / 2;
    private bool _restartPending;
    private bool _suspended;
    private bool _idleExpired;
    private int _idleVersion;

    /// <param name="openSoundFont">Opens the SoundFont stream; called on a pool thread, disposed after the load.</param>
    public PianoService(IPianoAudioOutput output, Func<Task<Stream>> openSoundFont, TimeSpan? idleDelay = null, TimeSpan? unloadDelay = null)
    {
        _unloadDelay = unloadDelay ?? IdleUnloadDelay;
        _output = output;
        _openSoundFont = openSoundFont;
        _idleDelay = idleDelay ?? IdleStopDelay;
        _output.Died += OnOutputDied;
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

            // A failed stream start keeps the engine: retrying only needs the stream, not the SoundFont.
            if (_state == PianoLoadState.NotLoaded || (_state == PianoLoadState.Failed && _engine is null))
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

    // Notes are dropped while the stream is down, so the command queue cannot grow without bound.
    // The sustain flag is different: it is kept here and re-applied whenever the engine restarts.
    public void NoteOn(int note, int velocity)
    {
        var engine = Engine;
        if (engine is null)
        {
            return;
        }

        if (_output.IsRunning)
        {
            engine.NoteOn(note, velocity);
        }
        else
        {
            RequestRestart();
        }
    }

    public void NoteOff(int note)
    {
        if (Engine is { } engine && _output.IsRunning)
        {
            engine.NoteOff(note);
        }
    }

    public void SetSustain(bool on)
    {
        PianoSynthEngine? engine;
        lock (_gate)
        {
            _sustain = on;
            engine = _engine;
        }

        engine?.SetSustain(on);
    }

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

    public void Silence()
    {
        PianoSynthEngine? engine;
        lock (_gate)
        {
            _sustain = false;
            engine = _engine;
        }

        engine?.Silence();
    }

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
            {
                soundFont = await ParseAsync(source).ConfigureAwait(false);
            }

            // The asset stream and any temporary copy are garbage now: collect them once, off the
            // UI thread, so the 8 MB buffer does not linger on the large object heap.
            GC.Collect();

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
                if (_sustain)
                {
                    engine.SetSustain(true);
                }

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

        // Released while loading: nobody will start the idle countdown, so start it now.
        int version;
        lock (_gate)
        {
            if (_leases > 0)
            {
                return;
            }

            version = ++_idleVersion;
        }

        _ = Task.Run(() => StopWhenIdleAsync(version));
    }

    // MeltySynth needs a seekable stream. A packaged asset usually is not, so it is copied once into a
    // buffer sized for the asset (no regrowth); a seekable stream (a file) is parsed in place.
    private static async Task<SoundFont> ParseAsync(Stream source)
    {
        if (source.CanSeek)
        {
            return new SoundFont(source);
        }

        using var buffer = new MemoryStream(ExpectedAssetBytes);
        await source.CopyToAsync(buffer).ConfigureAwait(false);
        buffer.Position = 0;
        return new SoundFont(buffer);
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

        try
        {
            await Task.Delay(_unloadDelay).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (Unload(version))
        {
            RaiseStateChanged();

            // Same as after the load: the SoundFont is garbage now, collect it off the UI thread.
            GC.Collect();
        }
    }

    // Drops the engine and the SoundFont when nobody came back since the countdown began. The audio
    // thread is stopped before the engine is released; an Acquire racing with this either cancels it
    // (version check under the gate) or finds NotLoaded and reloads.
    private bool Unload(int version)
    {
        try
        {
            lock (_outputGate)
            {
                lock (_gate)
                {
                    if (version != _idleVersion || _leases > 0 || _engine is null || _state == PianoLoadState.Loading)
                    {
                        return false;
                    }
                }

                if (_output.IsRunning)
                {
                    _output.Stop();
                }

                lock (_gate)
                {
                    if (version != _idleVersion || _leases > 0)
                    {
                        return false;
                    }

                    _engine = null;
                    _state = PianoLoadState.NotLoaded;
                    _idleExpired = false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Piano unload failed: {ex.Message}");
            return false;
        }
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
                bool sustain;
                lock (_gate)
                {
                    engine = _engine;
                    sustain = _sustain;
                    wanted = engine is not null && !_suspended && (_leases > 0 || !_idleExpired);
                }

                if (wanted && !_output.IsRunning)
                {
                    // Notes queued while stopped must not sound when the stream comes back.
                    engine!.Silence();
                    if (sustain)
                    {
                        engine.SetSustain(true);
                    }

                    SetStreamResult(_output.Start(engine));
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

    private void SetStreamResult(bool started)
    {
        var changed = false;
        lock (_gate)
        {
            if (!started && _state == PianoLoadState.Ready)
            {
                _state = PianoLoadState.Failed;
                changed = true;
            }
            else if (started && _state == PianoLoadState.Failed)
            {
                _state = PianoLoadState.Ready;
                changed = true;
            }
        }

        if (changed)
        {
            RaiseStateChanged();
        }
    }

    private void OnOutputDied(object? sender, EventArgs e) => RequestRestart();

    // Restarts the stream after it died or a note found it stopped; at most once per second.
    private void RequestRestart()
    {
        long wait;
        lock (_gate)
        {
            if (_leases == 0 || _suspended || _engine is null || _restartPending || _state == PianoLoadState.Failed)
            {
                return;
            }

            var now = Environment.TickCount64;
            wait = Math.Max(0, RestartThrottleMilliseconds - (now - _lastRestartTick));
            _lastRestartTick = now + wait;
            _restartPending = true;
        }

        Task.Run(async () =>
        {
            if (wait > 0)
            {
                await Task.Delay((int)wait).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _restartPending = false;
            }

            Reconcile();
        });
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
