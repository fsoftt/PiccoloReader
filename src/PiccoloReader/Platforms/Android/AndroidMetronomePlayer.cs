using Android.Content;
using Android.Media;
using Android.OS;
using CommunityToolkit.Maui.Alerts;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Platforms.Android;

/// <summary>
/// Metronome playback through one streaming AudioTrack fed by a dedicated
/// audio-priority thread. Nothing exists while stopped: the track and the
/// thread are created by <see cref="Start"/> and fully released on stop.
/// Click timing comes from <see cref="MetronomeScheduler"/> (frame indices),
/// never from timers.
/// </summary>
public sealed class AndroidMetronomePlayer : IMetronomePlayer
{
    private const int FallbackSampleRate = 48000;
    private const int ChunkMilliseconds = 20;
    private const int BufferChunks = 3;
    private const int PendingBeatCapacity = 16;
    private const int PreviousSessionJoinMilliseconds = 500;

    // Click waveforms for one sample rate, swapped as a whole so a reader
    // never sees a mismatched pair.
    private sealed record ClickCache(int SampleRate, ClickSet Clicks);

    private static volatile ClickCache? s_clickCache;

    private readonly object _gate = new();
    private volatile MetronomeSettings _settings = new();
    private volatile Session? _session;
    private Session? _previousSession;
    private FocusListener? _focusListener;
    private AudioFocusRequestClass? _focusRequest;

    public bool IsPlaying => _session is not null;

    public event Action<int, bool>? BeatTicked;

    public event EventHandler? IsPlayingChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            RequestAudioFocus();

            // The new thread waits for the previous one to release its track,
            // so two AudioTracks never coexist and the UI thread never blocks.
            _session = new Session(this, _previousSession);
            _previousSession = null;
        }

        RaiseIsPlayingChanged();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                return;
            }

            _session.RequestStop();
            _previousSession = _session;
            _session = null;
            AbandonAudioFocus();
        }

        RaiseIsPlayingChanged();
    }

    public void UpdateSettings(MetronomeSettings settings) => _settings = settings;

    private void RaiseIsPlayingChanged() =>
        MainThread.BeginInvokeOnMainThread(() => IsPlayingChanged?.Invoke(this, EventArgs.Empty));

    private void OnSessionFailed(Session session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            _session = null;
            AbandonAudioFocus();
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsPlayingChanged?.Invoke(this, EventArgs.Empty);
            Toast.Make(AppStrings.MetronomeStartFailed).Show();
        });
    }

    private bool IsCurrent(Session session) => ReferenceEquals(_session, session);

    private static ClickSet GetClicks(int sampleRate)
    {
        var cache = s_clickCache;
        if (cache is null || cache.SampleRate != sampleRate)
        {
            cache = new ClickCache(sampleRate, new ClickSet(sampleRate));
            s_clickCache = cache;
        }

        return cache.Clicks;
    }

    private static AudioAttributes CreateAttributes() =>
        new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Sonification)!
            .Build()!;

    // Held while playing; a lasting loss (e.g. an incoming call) stops it.
    private void RequestAudioFocus()
    {
        if (global::Android.App.Application.Context.GetSystemService(Context.AudioService) is not AudioManager manager)
        {
            return;
        }

        _focusListener = new FocusListener(OnAudioFocusChange);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(CreateAttributes())!
                .SetOnAudioFocusChangeListener(_focusListener)!
                .Build();
            manager.RequestAudioFocus(_focusRequest!);
        }
        else
        {
#pragma warning disable CA1422
            manager.RequestAudioFocus(_focusListener, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        }
    }

    private void AbandonAudioFocus()
    {
        if (_focusListener is null)
        {
            return;
        }

        if (global::Android.App.Application.Context.GetSystemService(Context.AudioService) is AudioManager manager)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                if (_focusRequest is not null)
                {
                    manager.AbandonAudioFocusRequest(_focusRequest);
                }
            }
            else
            {
#pragma warning disable CA1422
                manager.AbandonAudioFocus(_focusListener);
#pragma warning restore CA1422
            }
        }

        _focusRequest = null;
        _focusListener.Dispose();
        _focusListener = null;
    }

    private void OnAudioFocusChange(AudioFocus change)
    {
        // Ducking is ignored: a click track stays audible over a quiet notification.
        if (change is AudioFocus.Loss or AudioFocus.LossTransient)
        {
            Stop();
        }
    }

    private sealed class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly Action<AudioFocus> _onChange;

        public FocusListener(Action<AudioFocus> onChange) => _onChange = onChange;

        public void OnAudioFocusChange(AudioFocus focusChange) => _onChange(focusChange);
    }

    private sealed class Session
    {
        private readonly AndroidMetronomePlayer _owner;
        private Session? _previous;
        private readonly Thread _thread;
        private readonly object _trackGate = new();
        private readonly Action _deliverBeat;
        private volatile bool _stopRequested;
        private AudioTrack? _track;

        // Latest beat waiting for the UI thread (beat << 1 | accent); beats
        // are at least 240 ms apart, so coalescing never loses a visible beat.
        private int _uiBeat;
        private int _deliverQueued;

        public Session(AndroidMetronomePlayer owner, Session? previous)
        {
            _owner = owner;
            _previous = previous;
            _deliverBeat = DeliverBeat;
            _thread = new Thread(Run) { IsBackground = true, Name = "MetronomeAudio" };
            _thread.Start();
        }

        public void Join() => _thread.Join(PreviousSessionJoinMilliseconds);

        // Pause + Flush frees the track buffer, which unblocks a pending
        // Write and drops queued clicks so nothing sounds after Stop.
        public void RequestStop()
        {
            _stopRequested = true;
            lock (_trackGate)
            {
                if (_track is null)
                {
                    return;
                }

                try
                {
                    _track.Pause();
                    _track.Flush();
                }
                catch (Exception)
                {
                    // Track already stopped or released.
                }
            }
        }

        private void Run()
        {
            var failed = false;
            try
            {
                _previous?.Join();
                _previous = null;
                if (_stopRequested)
                {
                    return;
                }

                global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);
                Play();
            }
            catch (Exception ex)
            {
                failed = !_stopRequested;
                System.Diagnostics.Debug.WriteLine($"Metronome playback failed: {ex.Message}");
            }
            finally
            {
                ReleaseTrack();
                if (failed)
                {
                    _owner.OnSessionFailed(this);
                }
            }
        }

        private void Play()
        {
            var sampleRate = AudioTrack.GetNativeOutputSampleRate(global::Android.Media.Stream.Music);
            if (sampleRate <= 0)
            {
                sampleRate = FallbackSampleRate;
            }

            var scheduler = new MetronomeScheduler(sampleRate, GetClicks(sampleRate));
            var chunkFrames = sampleRate * ChunkMilliseconds / 1000;
            var chunk = new short[chunkFrames];
            var bufferBytes = Math.Max(
                AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Mono, Encoding.Pcm16bit),
                chunkFrames * sizeof(short) * BufferChunks);

            var track = CreateTrack(sampleRate, bufferBytes);
            lock (_trackGate)
            {
                if (_stopRequested)
                {
                    track.Release();
                    return;
                }

                _track = track;
            }

            track.Play();

            // Beats written but not yet heard, ordered by frame.
            var pendingFrames = new long[PendingBeatCapacity];
            var pendingBeats = new int[PendingBeatCapacity];
            var pendingAccents = new bool[PendingBeatCapacity];
            var pendingHead = 0;
            var pendingCount = 0;

            while (!_stopRequested)
            {
                scheduler.Fill(chunk, chunkFrames, _owner._settings);

                for (var i = 0; i < scheduler.BeatEventCount; i++)
                {
                    if (pendingCount == PendingBeatCapacity)
                    {
                        pendingHead = (pendingHead + 1) % PendingBeatCapacity;
                        pendingCount--;
                    }

                    var slot = (pendingHead + pendingCount) % PendingBeatCapacity;
                    var beat = scheduler.BeatEvents[i];
                    pendingFrames[slot] = beat.Frame;
                    pendingBeats[slot] = beat.BeatInBar;
                    pendingAccents[slot] = beat.IsAccent;
                    pendingCount++;
                }

                if (track.Write(chunk, 0, chunkFrames) < 0)
                {
                    if (!_stopRequested)
                    {
                        throw new InvalidOperationException("AudioTrack write failed.");
                    }

                    break;
                }

                // The write above blocks once the track buffer is full, so
                // this runs about once per chunk. A beat is announced when
                // the playback head reaches its frame, i.e. when it is
                // heard rather than when it was queued.
                if (pendingCount > 0)
                {
                    var head = (uint)track.PlaybackHeadPosition;
                    while (pendingCount > 0 && (int)(head - (uint)pendingFrames[pendingHead]) >= 0)
                    {
                        PostBeat(pendingBeats[pendingHead], pendingAccents[pendingHead]);
                        pendingHead = (pendingHead + 1) % PendingBeatCapacity;
                        pendingCount--;
                    }
                }
            }
        }

        private void ReleaseTrack()
        {
            lock (_trackGate)
            {
                if (_track is null)
                {
                    return;
                }

                try
                {
                    _track.Stop();
                }
                catch (Exception)
                {
                    // Already stopped or never started.
                }

                _track.Release();
                _track.Dispose();
                _track = null;
            }
        }

        // Only pay for the UI hop while the metronome screen listens. The
        // delegate is cached, so a beat allocates nothing.
        private void PostBeat(int beatInBar, bool isAccent)
        {
            if (_stopRequested || _owner.BeatTicked is null)
            {
                return;
            }

            Volatile.Write(ref _uiBeat, (beatInBar << 1) | (isAccent ? 1 : 0));
            if (Interlocked.Exchange(ref _deliverQueued, 1) == 0)
            {
                MainThread.BeginInvokeOnMainThread(_deliverBeat);
            }
        }

        private void DeliverBeat()
        {
            var packed = Volatile.Read(ref _uiBeat);
            Volatile.Write(ref _deliverQueued, 0);

            // A beat queued just before Stop must not light a dot afterwards.
            if (_stopRequested || !_owner.IsCurrent(this))
            {
                return;
            }

            _owner.BeatTicked?.Invoke(packed >> 1, (packed & 1) != 0);
        }

        private static AudioTrack CreateTrack(int sampleRate, int bufferBytes)
        {
            var format = new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetEncoding(Encoding.Pcm16bit)!
                .SetChannelMask(ChannelOut.Mono)!
                .Build()!;
            var builder = new AudioTrack.Builder()
                .SetAudioAttributes(CreateAttributes())!
                .SetAudioFormat(format)!
                .SetBufferSizeInBytes(bufferBytes)!
                .SetTransferMode(AudioTrackMode.Stream)!;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                builder.SetPerformanceMode(AudioTrackPerformanceMode.LowLatency);
            }

            return builder.Build();
        }
    }
}
