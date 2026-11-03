using Android.Content;
using Android.Media;
using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Platforms.Android;

/// <summary>
/// Piano playback through one float stereo AudioTrack in low-latency mode, fed by a dedicated
/// audio-priority thread that pulls <see cref="PianoSynthEngine"/> blocks. Nothing exists while
/// stopped. Audio focus is deliberately not requested: the metronome must keep playing under
/// the piano, and the piano is silent whenever the user is not touching it.
/// </summary>
public sealed class AndroidPianoAudioOutput : IPianoAudioOutput
{
    private const int FallbackSampleRate = 48000;
    private const int FallbackBurstFrames = 192;
    private const int MinBlockFrames = 128;
    private const int MaxBlockFrames = 256;
    private const int StopJoinMilliseconds = 1000;

    private readonly object _gate = new();

    // Serialises Render between a session and a stale one whose thread outlived its join timeout,
    // so two threads never drive the same engine at once.
    private readonly object _renderGate = new();
    private Session? _session;

    public event EventHandler? Died;

    public int PreferredSampleRate
    {
        get
        {
            var rate = AudioTrack.GetNativeOutputSampleRate(global::Android.Media.Stream.Music);
            return rate > 0 ? rate : FallbackSampleRate;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _session is { IsAlive: true };
            }
        }
    }

    public bool Start(PianoSynthEngine engine)
    {
        lock (_gate)
        {
            if (_session is { IsAlive: true })
            {
                return true;
            }

            _session?.Stop();
            try
            {
                _session = new Session(engine, GetBlockFrames(), _renderGate, OnSessionDied);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Piano playback failed to start: {ex.Message}");
                _session = null;
                return false;
            }
        }
    }

    private void OnSessionDied(Session session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }
        }

        Died?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        session?.Stop();
    }

    // One block per output burst keeps the engine's command latency near the device latency.
    private static int GetBlockFrames()
    {
        var burst = FallbackBurstFrames;
        if (global::Android.App.Application.Context.GetSystemService(Context.AudioService) is AudioManager manager
            && int.TryParse(manager.GetProperty(AudioManager.PropertyOutputFramesPerBuffer), out var parsed)
            && parsed > 0)
        {
            burst = parsed;
        }

        return Math.Clamp(burst, MinBlockFrames, MaxBlockFrames);
    }

    private sealed class Session
    {
        private readonly PianoSynthEngine _engine;
        private readonly int _blockFrames;
        private readonly AudioTrack _track;
        private readonly Thread _thread;
        private readonly object _renderGate;
        private readonly Action<Session> _onDied;
        private volatile bool _stopRequested;

        public Session(PianoSynthEngine engine, int blockFrames, object renderGate, Action<Session> onDied)
        {
            _engine = engine;
            _blockFrames = blockFrames;
            _renderGate = renderGate;
            _onDied = onDied;
            _track = CreateTrack(engine.SampleRate, blockFrames);
            try
            {
                _track.Play();
                _thread = new Thread(Run) { IsBackground = true, Name = "PianoAudio" };
                _thread.Start();
            }
            catch (Exception)
            {
                ReleaseTrack();
                throw;
            }
        }

        public bool IsAlive => !_stopRequested && _thread.IsAlive;

        // Pause + Flush frees the track buffer, which unblocks a pending Write.
        public void Stop()
        {
            _stopRequested = true;
            try
            {
                _track.Pause();
                _track.Flush();
            }
            catch (Exception)
            {
                // Track already stopped or released.
            }

            if (!_thread.Join(StopJoinMilliseconds))
            {
                System.Diagnostics.Debug.WriteLine("Piano audio thread did not stop in time.");
            }
        }

        private void Run()
        {
            try
            {
                global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);

                // All buffers are allocated once; the loop below allocates nothing.
                var left = new float[_blockFrames];
                var right = new float[_blockFrames];
                var interleaved = new float[_blockFrames * 2];
                var interleavedIsSilent = true;
                while (!_stopRequested)
                {
                    bool rendered;
                    lock (_renderGate)
                    {
                        // A stale session (its Stop timed out) must not render once a newer one owns the engine.
                        if (_stopRequested)
                        {
                            break;
                        }

                        rendered = _engine.Render(left, right);
                    }

                    if (rendered)
                    {
                        for (var i = 0; i < _blockFrames; i++)
                        {
                            interleaved[2 * i] = left[i];
                            interleaved[(2 * i) + 1] = right[i];
                        }

                        interleavedIsSilent = false;
                    }
                    else if (!interleavedIsSilent)
                    {
                        // Idle engine: zero the buffer once, then keep writing it as is.
                        Array.Clear(interleaved);
                        interleavedIsSilent = true;
                    }

                    if (_track.Write(interleaved, 0, interleaved.Length, WriteMode.Blocking) < 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Piano playback failed: {ex.Message}");
            }
            finally
            {
                var died = !_stopRequested;
                _stopRequested = true;
                ReleaseTrack();
                if (died)
                {
                    _onDied(this);
                }
            }
        }

        private void ReleaseTrack()
        {
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
        }

        private static AudioTrack CreateTrack(int sampleRate, int blockFrames)
        {
            var minBytes = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
            var bufferBytes = Math.Max(minBytes, blockFrames * 2 * sizeof(float) * 4);
            var attributes = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!;
            var format = new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetEncoding(Encoding.PcmFloat)!
                .SetChannelMask(ChannelOut.Stereo)!
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
