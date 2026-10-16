namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Glue between a platform audio input and the pitch pipeline. Feed it blocks of
/// mono samples from the audio thread; it decimates by 2, runs YIN every hop and
/// invokes the reading callback (on the calling thread) when the smoothed reading
/// meaningfully changes. Allocation-free after construction.
/// </summary>
public sealed class TunerEngine
{
    public const int WindowSize = 640;
    public const int HopSize = 512;
    private const int RingSize = 1024;
    private const float SilentPeak = 0.002f;
    private const int IdleHopsBeforeThrottle = 4;
    public const float MinEmitIntervalMs = 50f;
    public const float MinCentsChange = 0.5f;

    private const int InputChunk = 2048;
    private const float Pcm16Scale = 1f / 32768f;

    private readonly HalfBandDecimator _decimator = new();
    private readonly YinPitchDetector _detector;
    private readonly PitchSmoother _smoother;
    private readonly float[] _ring = new float[RingSize];
    private readonly TransientGate _transients = new();
    private readonly float[] _window = new float[WindowSize];
    private readonly float[] _decimated = new float[HalfBandDecimator.MaxOutputLength(InputChunk)];
    private readonly float[] _floatChunk = new float[InputChunk];
    private readonly float _hopMs;
    private int _writeIndex;
    private int _filled;
    private int _sinceHop;
    private float _sinceEmitMs = MinEmitIntervalMs;
    private TunerReading _lastEmitted;
    private TunerReading _lastReading;
    private int _noPitchHops;
    private bool _skipToggle;
    private readonly Action<TunerReading>? _onReading;
    private volatile bool _resetPending;

    /// <param name="inputSampleRate">Input rate in Hz; at least 32000.</param>
    /// <param name="onReading">Invoked on the thread calling ProcessBlock.</param>
    public TunerEngine(int inputSampleRate, Action<TunerReading>? onReading = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputSampleRate, 32000);
        var decimatedRate = inputSampleRate / 2f;
        _detector = new YinPitchDetector(decimatedRate, WindowSize);
        _hopMs = HopSize * 1000f / decimatedRate;
        _smoother = new PitchSmoother(_hopMs);
        _onReading = onReading;
    }

    /// <summary>
    /// Safe from any thread. Only raises a flag; the audio thread clears the
    /// state at the start of its next ProcessBlock.
    /// </summary>
    public void Reset() => _resetPending = true;

    /// <summary>Lags evaluated by the pitch detector so far; a proxy for the CPU spent.</summary>
    public long LagEvaluations => _detector.LagEvaluations;

    private void ApplyPendingReset()
    {
        if (!_resetPending)
        {
            return;
        }

        _resetPending = false;
        _decimator.Reset();
        _smoother.Reset();
        _detector.Reset();
        _transients.Reset();
        Array.Clear(_ring);
        _writeIndex = 0;
        _filled = 0;
        _sinceHop = 0;
        _sinceEmitMs = MinEmitIntervalMs;
        _lastEmitted = default;
        _lastReading = default;
        _noPitchHops = 0;
    }

    public void ProcessBlock(ReadOnlySpan<float> samples)
    {
        ApplyPendingReset();

        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, InputChunk);
            Consume(samples[..take]);
            samples = samples[take..];
        }
    }

    public void ProcessBlock(ReadOnlySpan<short> samples)
    {
        ApplyPendingReset();

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
        // A chunk far below the RMS gate cannot hold a note: skip the filter and
        // feed zeros (the cheapest path, taken for all the time spent in silence).
        var peak = 0f;
        for (var i = 0; i < chunk.Length; i++)
        {
            var v = chunk[i];
            if (v > peak)
            {
                peak = v;
            }
            else if (-v > peak)
            {
                peak = -v;
            }
        }

        var count = peak < SilentPeak
            ? _decimator.ProcessSilence(chunk.Length, _decimated)
            : _decimator.Process(chunk, _decimated);
        var done = 0;
        while (done < count)
        {
            // Copy up to the next hop boundary or the end of the ring, whichever is first.
            var run = Math.Min(count - done, Math.Min(HopSize - _sinceHop, RingSize - _writeIndex));
            Array.Copy(_decimated, done, _ring, _writeIndex, run);
            done += run;
            _writeIndex = (_writeIndex + run) & (RingSize - 1);
            _filled = Math.Min(WindowSize, _filled + run);
            _sinceHop += run;
            if (_sinceHop >= HopSize)
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
        // The window is the newest WindowSize samples of the ring.
        var start = (_writeIndex - WindowSize) & (RingSize - 1);
        var tail = Math.Min(WindowSize, RingSize - start);
        _ring.AsSpan(start, tail).CopyTo(_window);
        _ring.AsSpan(0, WindowSize - tail).CopyTo(_window.AsSpan(tail));

        var rms = _detector.Prepare(_window);
        _sinceEmitMs += _hopMs;
        if (_transients.IsTransient(rms))
        {
            // A click or tap corrupted this window: leave the smoother (and the
            // displayed note) exactly as they are.
            return;
        }

        // Nothing is shown and the last frames found no pitch (noise, speech): the
        // full lag search is the expensive case, so look only every other hop.
        if (!_lastReading.HasPitch && _noPitchHops >= IdleHopsBeforeThrottle && (_skipToggle = !_skipToggle))
        {
            return;
        }

        var estimate = _detector.Estimate(rms);
        _noPitchHops = estimate.HasPitch ? 0 : _noPitchHops + 1;
        var reading = _smoother.Update(estimate);
        _lastReading = reading;

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
        _onReading?.Invoke(reading);
    }
}
