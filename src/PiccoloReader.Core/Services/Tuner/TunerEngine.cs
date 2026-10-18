using System.Numerics;

namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Glue between a platform audio input and the pitch pipeline. Feed it blocks of
/// mono samples from the audio thread; it decimates by 2 (high band, piccolo down
/// to 400 Hz) and by 8 more (low band, tuba up from 25 Hz), runs one YIN per band
/// every hop, picks one result (<see cref="BandSelector"/>) and invokes the reading
/// callback (on the calling thread) when the smoothed reading meaningfully
/// changes. Allocation-free after construction.
/// </summary>
public sealed class TunerEngine
{
    public const int WindowSize = 640;
    public const int HopSize = 512;
    public const int LowBandStages = 3;
    public const int LowHopSize = HopSize >> LowBandStages;
    public const float LowBandMinHz = 25f;
    public const float LowBandMaxHz = 500f;
    public const float HighBandMinHz = 400f;
    public const float HighBandMaxHz = 4500f;
    public const float LowBandCutoffHz = 450f;
    public const int LowBandFullSearchEvery = 8;
    public const float HighBandSubMultipleThreshold = 0.6f;
    private const int RingSize = 1024;
    private const float SilentPeak = 0.002f;
    private const int IdleHopsBeforeThrottle = 4;
    public const float MinEmitIntervalMs = 50f;
    public const float MinCentsChange = 0.5f;

    private const int InputChunk = 2048;
    private const float Pcm16Scale = 1f / 32768f;

    private readonly HalfBandDecimator _decimator = new();
    private readonly YinPitchDetector _detector;
    private readonly YinPitchDetector _lowDetector;
    private readonly HalfBandDecimator[] _lowDecimators = [new(), new(), new()];
    private readonly LowBandFilter _lowFilter;
    private readonly BandSelector _selector = new();
    private readonly TransientGate _lowTransients = new();
    private readonly float[] _lowRing;
    private readonly float[] _lowWindow;
    private readonly float[][] _lowStages;
    private readonly int _lowWindowSize;
    private int _lowWrite;
    private int _lowFilled;
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
        _detector = new YinPitchDetector(decimatedRate, WindowSize, HighBandMinHz, HighBandMaxHz, subMultipleThreshold: HighBandSubMultipleThreshold);
        var lowRate = decimatedRate / (1 << LowBandStages);
        _lowWindowSize = 3 * (int)(lowRate / LowBandMinHz);
        _lowDetector = new YinPitchDetector(lowRate, _lowWindowSize, LowBandMinHz, LowBandMaxHz, interpolateDips: true, fullSearchEvery: LowBandFullSearchEvery);
        _lowFilter = new LowBandFilter(lowRate, LowBandCutoffHz);
        _lowRing = new float[(int)BitOperations.RoundUpToPowerOf2((uint)_lowWindowSize)];
        _lowWindow = new float[_lowWindowSize];
        _lowStages = new float[LowBandStages][];
        var stageLength = HopSize;
        for (var i = 0; i < LowBandStages; i++)
        {
            stageLength = HalfBandDecimator.MaxOutputLength(stageLength);
            _lowStages[i] = new float[stageLength];
        }

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
    public long LagEvaluations => _detector.LagEvaluations
        + (long)(_lowDetector.LagEvaluations * _lowDetector.IntegrationLength / (double)_detector.IntegrationLength);

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
        foreach (var stage in _lowDecimators)
        {
            stage.Reset();
        }

        _lowDetector.Reset();
        _lowFilter.Reset();
        _lowTransients.Reset();
        _selector.Reset();
        Array.Clear(_lowRing);
        _lowWrite = 0;
        _lowFilled = 0;
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

        var silent = peak < SilentPeak;
        var count = silent
            ? _decimator.ProcessSilence(chunk.Length, _decimated)
            : _decimator.Process(chunk, _decimated);
        var done = 0;
        while (done < count)
        {
            // Copy up to the next hop boundary or the end of the ring, whichever is first.
            var run = Math.Min(count - done, Math.Min(HopSize - _sinceHop, RingSize - _writeIndex));
            Array.Copy(_decimated, done, _ring, _writeIndex, run);
            FeedLowBand(_decimated.AsSpan(done, run), silent);
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

    /// <summary>Runs the 24 kHz samples through the three decimators into the low-band ring.</summary>
    private void FeedLowBand(ReadOnlySpan<float> input, bool silent)
    {
        var count = input.Length;
        var source = input;
        for (var i = 0; i < LowBandStages; i++)
        {
            var output = _lowStages[i];
            count = silent
                ? _lowDecimators[i].ProcessSilence(count, output)
                : _lowDecimators[i].Process(source, output);
            source = output.AsSpan(0, count);
        }

        var filtered = _lowStages[LowBandStages - 1].AsSpan(0, count);
        _lowFilter.Process(filtered);
        var ring = _lowRing;
        for (var i = 0; i < count; i++)
        {
            ring[_lowWrite] = filtered[i];
            _lowWrite = (_lowWrite + 1) & (ring.Length - 1);
        }

        _lowFilled = Math.Min(_lowWindowSize, _lowFilled + count);
    }

    private void Analyze()
    {
        // The window is the newest WindowSize samples of the ring.
        var start = (_writeIndex - WindowSize) & (RingSize - 1);
        var tail = Math.Min(WindowSize, RingSize - start);
        _ring.AsSpan(start, tail).CopyTo(_window);
        _ring.AsSpan(0, WindowSize - tail).CopyTo(_window.AsSpan(tail));

        var rms = _detector.Prepare(_window);
        var lowReady = _lowFilled == _lowWindowSize;
        var lowRms = 0f;
        if (lowReady)
        {
            var ring = _lowRing;
            var lowStart = (_lowWrite - _lowWindowSize) & (ring.Length - 1);
            var lowTail = Math.Min(_lowWindowSize, ring.Length - lowStart);
            ring.AsSpan(lowStart, lowTail).CopyTo(_lowWindow);
            ring.AsSpan(0, _lowWindowSize - lowTail).CopyTo(_lowWindow.AsSpan(lowTail));
            lowRms = _lowDetector.Prepare(_lowWindow);
        }

        _sinceEmitMs += _hopMs;
        var transient = _transients.IsTransient(rms);
        if (lowReady && _lowTransients.IsTransient(lowRms))
        {
            transient = true;
        }

        if (transient)
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

        var low = lowReady ? _lowDetector.Estimate(lowRms) : PitchEstimate.None(0f);

        var high = _detector.Estimate(rms);
        var estimate = _selector.Select(low, high) == TunerBand.Low ? low : high;
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
