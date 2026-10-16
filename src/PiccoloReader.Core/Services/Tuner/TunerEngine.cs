namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Glue between a platform audio input and the pitch pipeline. Feed it blocks of
/// mono samples from the audio thread; it decimates by 2, runs YIN every hop and
/// invokes <see cref="Reading"/> (on the calling thread) when the smoothed reading
/// meaningfully changes. Allocation-free after construction.
/// </summary>
public sealed class TunerEngine
{
    public const int WindowSize = 1024;
    public const int HopSize = 512;
    public const float MinEmitIntervalMs = 50f;
    public const float MinCentsChange = 0.5f;

    private const int InputChunk = 2048;
    private const float Pcm16Scale = 1f / 32768f;

    private readonly HalfBandDecimator _decimator = new();
    private readonly YinPitchDetector _detector;
    private readonly PitchSmoother _smoother;
    private readonly float[] _ring = new float[WindowSize];
    private readonly float[] _window = new float[WindowSize];
    private readonly float[] _decimated = new float[HalfBandDecimator.MaxOutputLength(InputChunk)];
    private readonly float[] _floatChunk = new float[InputChunk];
    private readonly float _hopMs;
    private int _writeIndex;
    private int _filled;
    private int _sinceHop;
    private float _sinceEmitMs = MinEmitIntervalMs;
    private TunerReading _lastEmitted;
    private TunerSettings _settings;

    public TunerEngine(int inputSampleRate, TunerSettings? settings = null, Action<TunerReading>? onReading = null)
    {
        var decimatedRate = inputSampleRate / 2f;
        _detector = new YinPitchDetector(decimatedRate, WindowSize);
        _hopMs = HopSize * 1000f / decimatedRate;
        _smoother = new PitchSmoother(_hopMs);
        _settings = settings ?? new TunerSettings();
        Reading = onReading;
    }

    /// <summary>Invoked on the thread calling ProcessBlock. Assign once, before processing.</summary>
    public Action<TunerReading>? Reading { get; set; }

    public TunerSettings Settings => Volatile.Read(ref _settings);

    /// <summary>Safe from any thread; takes effect on the next block.</summary>
    public void UpdateSettings(TunerSettings settings) => Volatile.Write(ref _settings, settings);

    public void Reset()
    {
        _decimator.Reset();
        _smoother.Reset();
        Array.Clear(_ring);
        _writeIndex = 0;
        _filled = 0;
        _sinceHop = 0;
        _sinceEmitMs = MinEmitIntervalMs;
        _lastEmitted = default;
    }

    public void ProcessBlock(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, InputChunk);
            Consume(samples[..take]);
            samples = samples[take..];
        }
    }

    public void ProcessBlock(ReadOnlySpan<short> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, InputChunk);
            for (var i = 0; i < take; i++)
            {
                _floatChunk[i] = samples[i] * Pcm16Scale;
            }

            Consume(_floatChunk.AsSpan(0, take));
            samples = samples[take..];
        }
    }

    private void Consume(ReadOnlySpan<float> chunk)
    {
        var count = _decimator.Process(chunk, _decimated);
        for (var i = 0; i < count; i++)
        {
            _ring[_writeIndex] = _decimated[i];
            _writeIndex = (_writeIndex + 1) & (WindowSize - 1);
            if (_filled < WindowSize)
            {
                _filled++;
            }

            if (++_sinceHop >= HopSize)
            {
                _sinceHop = 0;
                if (_filled == WindowSize)
                {
                    Analyze();
                }
            }
        }
    }

    private void Analyze()
    {
        // _writeIndex points at the oldest sample.
        var tail = WindowSize - _writeIndex;
        _ring.AsSpan(_writeIndex, tail).CopyTo(_window);
        _ring.AsSpan(0, _writeIndex).CopyTo(_window.AsSpan(tail));

        var estimate = _detector.Detect(_window);
        var reading = _smoother.Update(estimate);

        _sinceEmitMs += _hopMs;
        if (_sinceEmitMs < MinEmitIntervalMs)
        {
            return;
        }

        var changed = reading.HasPitch != _lastEmitted.HasPitch
            || (reading.HasPitch && MathF.Abs(reading.CentsFromA4 - _lastEmitted.CentsFromA4) >= MinCentsChange);
        if (!changed)
        {
            return;
        }

        _sinceEmitMs = 0f;
        _lastEmitted = reading;
        Reading?.Invoke(reading);
    }
}
