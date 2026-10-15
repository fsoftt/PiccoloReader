using Android.Media;
using Android.OS;
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

    private static ClickSet? s_clicks;
    private static int s_clickSampleRate;

    private readonly object _gate = new();
    private volatile MetronomeSettings _settings = new();
    private volatile Session? _session;

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

            _session = new Session(this);
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
            _session = null;
        }

        RaiseIsPlayingChanged();
    }

    public void UpdateSettings(MetronomeSettings settings) => _settings = settings;

    private void RaiseIsPlayingChanged() =>
        MainThread.BeginInvokeOnMainThread(() => IsPlayingChanged?.Invoke(this, EventArgs.Empty));

    private void OnSessionEnded(Session session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            _session = null;
        }

        RaiseIsPlayingChanged();
    }

    private static ClickSet GetClicks(int sampleRate)
    {
        if (s_clicks is null || s_clickSampleRate != sampleRate)
        {
            s_clicks = new ClickSet(sampleRate);
            s_clickSampleRate = sampleRate;
        }

        return s_clicks;
    }

    private sealed class Session
    {
        private readonly AndroidMetronomePlayer _owner;
        private volatile bool _stopRequested;

        public Session(AndroidMetronomePlayer owner)
        {
            _owner = owner;
            new Thread(Run) { IsBackground = true, Name = "MetronomeAudio" }.Start();
        }

        public void RequestStop() => _stopRequested = true;

        private void Run()
        {
            AudioTrack? track = null;
            try
            {
                global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);

                var sampleRate = AudioTrack.GetNativeOutputSampleRate(global::Android.Media.Stream.Music);
                if (sampleRate <= 0)
                {
                    sampleRate = FallbackSampleRate;
                }

                var clicks = GetClicks(sampleRate);
                var scheduler = new MetronomeScheduler(sampleRate, clicks);
                var chunkFrames = sampleRate * ChunkMilliseconds / 1000;
                var chunk = new short[chunkFrames];
                var bufferBytes = Math.Max(
                    AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Mono, Encoding.Pcm16bit),
                    chunkFrames * sizeof(short) * BufferChunks);

                track = CreateTrack(sampleRate, bufferBytes);
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
                            RaiseBeat(pendingBeats[pendingHead], pendingAccents[pendingHead]);
                            pendingHead = (pendingHead + 1) % PendingBeatCapacity;
                            pendingCount--;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metronome playback failed: {ex.Message}");
            }
            finally
            {
                if (track is not null)
                {
                    try
                    {
                        track.Stop();
                    }
                    catch (Exception)
                    {
                        // Already stopped or never started.
                    }

                    track.Release();
                    track.Dispose();
                }

                _owner.OnSessionEnded(this);
            }
        }

        private void RaiseBeat(int beatInBar, bool isAccent)
        {
            // Only pay for the UI hop while the metronome screen listens.
            if (_stopRequested || _owner.BeatTicked is null)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => _owner.BeatTicked?.Invoke(beatInBar, isAccent));
        }

        private static AudioTrack CreateTrack(int sampleRate, int bufferBytes)
        {
            var attributes = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Sonification)!
                .Build()!;
            var format = new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetEncoding(Encoding.Pcm16bit)!
                .SetChannelMask(ChannelOut.Mono)!
                .Build()!;
            var builder = new AudioTrack.Builder()
                .SetAudioAttributes(attributes)!
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
