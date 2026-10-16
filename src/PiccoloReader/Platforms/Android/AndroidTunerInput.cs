using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using AndroidX.Core.Content;
using Java.Nio;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Platforms.Android;

/// <summary>
/// Microphone capture for the tuner: one AudioRecord (PCM float, mono) read by a
/// dedicated audio-priority thread. Nothing exists while stopped: the record,
/// the buffers and the thread are created by <see cref="Start"/> and fully
/// released by <see cref="Stop"/>. The read loop reuses one direct ByteBuffer
/// and one float[] (no allocations) and feeds <see cref="TunerEngine"/>, which
/// decimates to half the input rate (24 kHz) and analyses 1024-sample windows
/// every 512 samples.
/// </summary>
public sealed class AndroidTunerInput : ITunerInput
{
    private const int PreferredSampleRate = 48000;
    private const int FallbackSampleRate = 44100;
    private const int ChunkSamples = 1024;
    private const int BytesPerSample = 4;
    private const int StopJoinMilliseconds = 1000;
    private const int ReadBlocking = 0; // AudioRecord.READ_BLOCKING

    private readonly object _gate = new();
    private Session? _session;

    public bool IsRunning => _session is not null;

    public event EventHandler<TunerReading>? ReadingChanged;

    public TunerStartResult Start()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return TunerStartResult.Started;
            }

            var context = global::Android.App.Application.Context;
            if (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                return TunerStartResult.PermissionDenied;
            }

            var source = SupportsUnprocessed(context) ? AudioSource.Unprocessed : AudioSource.VoiceRecognition;
            var record = TryCreateRecord(source, PreferredSampleRate) ?? TryCreateRecord(source, FallbackSampleRate);
            if (record is null)
            {
                return TunerStartResult.Failed;
            }

            try
            {
                record.StartRecording();
                if (record.RecordingState != RecordState.Recording)
                {
                    record.Release();
                    record.Dispose();
                    return TunerStartResult.Failed;
                }
            }
            catch (Exception)
            {
                record.Release();
                record.Dispose();
                return TunerStartResult.Failed;
            }

            _session = new Session(this, record);
            return TunerStartResult.Started;
        }
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

    private static bool SupportsUnprocessed(Context context)
    {
        try
        {
            var audioManager = (AudioManager?)context.GetSystemService(Context.AudioService);
            return audioManager?.GetProperty(AudioManager.PropertySupportAudioSourceUnprocessed) == "true";
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static AudioRecord? TryCreateRecord(AudioSource source, int sampleRate)
    {
        AudioRecord? record = null;
        try
        {
            var minBytes = AudioRecord.GetMinBufferSize(sampleRate, ChannelIn.Mono, Encoding.PcmFloat);
            if (minBytes <= 0)
            {
                return null;
            }

            var bufferBytes = Math.Max(minBytes, ChunkSamples * BytesPerSample * 4);
            record = new AudioRecord.Builder()!
                .SetAudioSource(source)!
                .SetAudioFormat(new AudioFormat.Builder()
                    .SetEncoding(Encoding.PcmFloat)!
                    .SetSampleRate(sampleRate)!
                    .SetChannelMask((ChannelOut)(int)ChannelIn.Mono)!
                    .Build()!)!
                .SetBufferSizeInBytes(bufferBytes)!
                .Build()!;
            if (record.State == global::Android.Media.State.Initialized)
            {
                return record;
            }

            record.Release();
            record.Dispose();
            return null;
        }
        catch (Exception)
        {
            record?.Release();
            record?.Dispose();
            return null;
        }
    }

    private void Raise(TunerReading reading) => ReadingChanged?.Invoke(this, reading);

    private sealed class Session
    {
        private readonly AndroidTunerInput _owner;
        private readonly AudioRecord _record;
        private readonly Thread _thread;
        private volatile bool _stopRequested;

        public Session(AndroidTunerInput owner, AudioRecord record)
        {
            _owner = owner;
            _record = record;
            _thread = new Thread(Run) { IsBackground = true, Name = "TunerAudioInput" };
            _thread.Start();
        }

        public void Stop()
        {
            _stopRequested = true;
            try
            {
                // Makes a blocked Read return so the thread can exit.
                _record.Stop();
            }
            catch (Exception)
            {
                // The thread releases the record anyway.
            }

            if (Thread.CurrentThread != _thread)
            {
                _thread.Join(StopJoinMilliseconds);
            }
        }

        private void Run()
        {
            ByteBuffer? buffer = null;
            var failed = false;
            try
            {
                Process.SetThreadPriority(global::Android.OS.ThreadPriority.UrgentAudio);

                buffer = ByteBuffer.AllocateDirect(ChunkSamples * BytesPerSample)!;
                buffer.Order(ByteOrder.NativeOrder()!);
                var address = global::Android.Runtime.JNIEnv.GetDirectBufferAddress(buffer.Handle);
                var samples = new float[ChunkSamples];
                var engine = new TunerEngine(_record.SampleRate, _owner.Raise);

                while (!_stopRequested)
                {
                    var bytes = _record.Read(buffer, ChunkSamples * BytesPerSample, ReadBlocking);
                    if (_stopRequested)
                    {
                        break;
                    }

                    if (bytes <= 0)
                    {
                        failed = true;
                        break;
                    }

                    var count = bytes / BytesPerSample;
                    System.Runtime.InteropServices.Marshal.Copy(address, samples, 0, count);
                    engine.ProcessBlock(samples.AsSpan(0, count));
                }
            }
            catch (Exception)
            {
                failed = true;
            }
            finally
            {
                try
                {
                    _record.Stop();
                }
                catch (Exception)
                {
                }

                _record.Release();
                _record.Dispose();
                buffer?.Dispose();
            }

            if (failed && !_stopRequested)
            {
                // Input lost (for example another app took the microphone): show silence.
                lock (_owner._gate)
                {
                    if (ReferenceEquals(_owner._session, this))
                    {
                        _owner._session = null;
                    }
                }

                _owner.Raise(default);
            }
        }
    }
}
