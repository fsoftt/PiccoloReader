namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// YIN pitch detector tuned for a cheap per-frame cost on slow devices:
/// an RMS gate skips silent windows before any lag is evaluated; the lag range
/// comes from the minHz/maxHz arguments (the tuner runs one instance for 400 Hz to
/// 4.5 kHz and one for 25 to 500 Hz) and the integration length is the
/// window minus the longest lag; a full search stops at the first dip under the
/// threshold; and once a pitch is found, the next frames only verify five lags
/// around it (about 10x cheaper) and fall back to the full search when the
/// check fails. Every buffer is preallocated.
/// </summary>
public sealed class YinPitchDetector
{
    private const int TrackRadius = 2;
    private const int MaxSubMultiple = 4;

    private readonly float _sampleRate;
    private readonly int _windowSize;
    private readonly int _integrationLength;
    private readonly int _tauMin;
    private readonly int _tauMax;
    private readonly float _threshold;
    private readonly float _rmsGate;
    private readonly bool _interpolateDips;
    private readonly float _subMultipleThreshold;
    private readonly int _fullSearchEvery;
    private int _trackedFrames;
    private readonly float[] _centered;
    private readonly float[] _difference;
    private readonly float[] _cmndf;
    private int _trackedLag;
    private int _trackCount;

    public YinPitchDetector(float sampleRate, int windowSize = 640, float minHz = 400f, float maxHz = 4500f, float threshold = 0.13f, float rmsGate = 0.005f, bool interpolateDips = false, float subMultipleThreshold = 0f, int fullSearchEvery = 0)
    {
        _fullSearchEvery = fullSearchEvery;
        _subMultipleThreshold = Math.Max(threshold, subMultipleThreshold);
        _interpolateDips = interpolateDips;
        _sampleRate = sampleRate;
        _windowSize = windowSize;
        _threshold = threshold;
        _rmsGate = rmsGate;
        _tauMax = (int)(sampleRate / minHz);
        _tauMin = Math.Max(2, (int)(sampleRate / maxHz));
        _integrationLength = windowSize - _tauMax;
        if (_integrationLength <= 0 || _tauMin >= _tauMax - TrackRadius)
        {
            throw new ArgumentException("Window too short for the requested pitch range.");
        }

        _centered = new float[windowSize];
        _difference = new float[_tauMax + 2];
        _cmndf = new float[_tauMax + 2];
    }

    public int WindowSize => _windowSize;

    /// <summary>Samples compared per lag.</summary>
    public int IntegrationLength => _integrationLength;

    /// <summary>Number of lags evaluated so far (each costs one integration length of multiply-adds).</summary>
    public long LagEvaluations { get; private set; }

    public void Reset() => _trackedLag = 0;

    public PitchEstimate Detect(ReadOnlySpan<float> window) => Estimate(Prepare(window));

    /// <summary>Removes DC from the window and returns its RMS. Call before <see cref="Estimate"/>.</summary>
    public float Prepare(ReadOnlySpan<float> window)
    {
        if (window.Length < _windowSize)
        {
            return 0f;
        }

        var sum = 0f;
        for (var i = 0; i < _windowSize; i++)
        {
            sum += window[i];
        }

        var mean = sum / _windowSize;
        var energy = 0f;
        var x = _centered;
        for (var i = 0; i < _windowSize; i++)
        {
            var v = window[i] - mean;
            x[i] = v;
            energy += v * v;
        }

        return MathF.Sqrt(energy / _windowSize);
    }

    /// <summary>Pitch of the window last passed to <see cref="Prepare"/>, which returned <paramref name="rms"/>.</summary>
    public PitchEstimate Estimate(float rms)
    {
        if (rms < _rmsGate)
        {
            _trackedLag = 0;
            return PitchEstimate.None(rms);
        }

        // A lag that stays clean can be a multiple of the period (a sine repeats every
        // period); a periodic full search lets the first dip win again.
        if (_trackedLag > 0 && (_fullSearchEvery == 0 || ++_trackedFrames < _fullSearchEvery) && TryTrack(rms, out var tracked))
        {
            return tracked;
        }

        _trackedFrames = 0;

        return FullSearch(rms);
    }

    private bool TryTrack(float rms, out PitchEstimate estimate)
    {
        estimate = default;
        var low = _trackedLag - TrackRadius;
        var high = _trackedLag + TrackRadius;
        if (low <= _tauMin || high >= _tauMax)
        {
            return false;
        }

        var best = low;
        for (var tau = low; tau <= high; tau++)
        {
            ComputeDifference(tau);
            if (_difference[tau] < _difference[best])
            {
                best = tau;
            }
        }

        // A minimum on the edge of the range is not a minimum: the pitch moved.
        if (best == low || best == high)
        {
            return false;
        }

        var normalised = Normalised(best);
        if (normalised >= _threshold)
        {
            return false;
        }

        var clarity = 1f - normalised;
        if ((++_trackCount & 3) == 0)
        {
            best = ShortestSubMultiple(best, ref clarity);
        }

        _trackedLag = best;
        estimate = new PitchEstimate(_sampleRate / (best + Refine(best)), clarity, rms, true);
        return true;
    }

    /// <summary>Difference at the lag relative to the energy of both compared segments: 0 for a perfectly periodic signal, about 1 for noise.</summary>
    private float Normalised(int lag)
    {
        var x = _centered;
        float e0 = 0f, eLag = 0f;
        for (var j = 0; j < _integrationLength; j++)
        {
            var a = x[j];
            var b = x[lag + j];
            e0 += a * a;
            eLag += b * b;
        }

        var denominator = e0 + eLag;
        if (denominator <= 0f)
        {
            return 1f;
        }

        var d = _difference[lag];
        if (_interpolateDips)
        {
            d = DipMinimum(_difference[lag - 1], d, _difference[lag + 1]);
        }

        return d / denominator;
    }

    /// <summary>
    /// Value at the bottom of the parabola through three neighbouring lags. At a low
    /// sample rate the true period falls between two lags and the integer lag
    /// alone looks worse than a multiple of the period that happens to land closer.
    /// </summary>
    private static float DipMinimum(float before, float center, float after)
    {
        var curvature = before - 2f * center + after;
        if (curvature <= 0f)
        {
            return center;
        }

        var slope = before - after;
        return Math.Max(0f, center - (slope * slope) / (8f * curvature));
    }

    private PitchEstimate FullSearch(float rms)
    {
        var d = _difference;
        var cm = _cmndf;
        var running = 0f;
        var chosen = 0;
        var interpolated = -1f;

        for (var tau = 1; tau <= _tauMax; tau++)
        {
            ComputeDifference(tau);
            running += d[tau];
            cm[tau] = running > 0f ? d[tau] * tau / running : 1f;

            if (_interpolateDips && tau - 2 >= _tauMin && cm[tau - 1] <= cm[tau - 2] && cm[tau - 1] < cm[tau])
            {
                var dip = DipMinimum(cm[tau - 2], cm[tau - 1], cm[tau]);
                if (dip < _threshold)
                {
                    chosen = tau - 1;
                    interpolated = dip;
                    break;
                }
            }

            if (tau < _tauMin || cm[tau] >= _threshold)
            {
                continue;
            }

            // Follow the dip down to its local minimum, then stop computing.
            chosen = tau;
            while (chosen < _tauMax)
            {
                var next = chosen + 1;
                ComputeDifference(next);
                running += d[next];
                cm[next] = running > 0f ? d[next] * next / running : 1f;
                if (cm[next] >= cm[chosen])
                {
                    break;
                }

                chosen = next;
            }

            break;
        }

        // A dip on the last lag is not a minimum (the next lag was never seen): the
        // pitch is below the range, not at its edge.
        if (chosen == 0 || chosen >= _tauMax)
        {
            _trackedLag = 0;
            return PitchEstimate.None(rms);
        }

        // The CMNDF minimum can sit one lag off the minimum of d itself; refine
        // around the lag with the smallest difference.
        var lag = chosen;
        if (chosen < _tauMax)
        {
            var low = Math.Max(1, chosen - 1);
            var high = Math.Min(chosen + 1, _tauMax - 1);
            for (var candidate = low; candidate <= high; candidate++)
            {
                if (d[candidate] < d[lag])
                {
                    lag = candidate;
                }
            }

            if (lag + 1 > chosen + 1)
            {
                ComputeDifference(lag + 1);
            }
        }

        var clarity = 1f - (interpolated >= 0f ? interpolated : cm[chosen]);
        lag = ShortestSubMultiple(lag, ref clarity);


        _trackedLag = lag;
        var offset = lag < _tauMax ? Refine(lag) : 0f;
        var hz = _sampleRate / (lag + offset);
        return new PitchEstimate(hz, clarity, rms, true);
    }

    /// <summary>
    /// A window damaged by a click can dip first at a multiple of the period (a
    /// sine repeats at 2x, 3x...). Looks for a clean dip at 1/2, 1/3 and 1/4 of the
    /// lag and returns the shortest one found, or the lag itself.
    /// </summary>
    private int ShortestSubMultiple(int lag, ref float clarity)
    {
        var result = lag;
        for (var k = 2; k <= MaxSubMultiple; k++)
        {
            var sub = (int)MathF.Round(lag / (float)k);
            if (sub - TrackRadius <= _tauMin || sub + TrackRadius >= lag)
            {
                break;
            }

            var best = sub - TrackRadius;
            for (var tau = best; tau <= sub + TrackRadius; tau++)
            {
                ComputeDifference(tau);
                if (_difference[tau] < _difference[best])
                {
                    best = tau;
                }
            }

            if (best == sub - TrackRadius || best == sub + TrackRadius)
            {
                continue;
            }

            var normalised = Normalised(best);
            if (normalised < _threshold)
            {
                result = best;
                clarity = 1f - normalised;
            }
            else if (normalised < _subMultipleThreshold)
            {
                // Not a clean dip, yet far deeper than a lag that is not a period would
                // give: the lag found is a multiple of the period. Keeps its clarity.
                result = best;
            }
        }

        return result;
    }

    private void ComputeDifference(int tau)
    {
        LagEvaluations++;
        var x = _centered;
        var n = _integrationLength;
        var acc = 0f;
        for (var j = 0; j < n; j++)
        {
            var diff = x[j] - x[j + tau];
            acc += diff * diff;
        }

        _difference[tau] = acc;
    }

    /// <summary>
    /// Sub-sample lag offset from a parabola through the difference function,
    /// corrected for the cosine shape of d(tau) of a sinusoid. A plain parabola
    /// is ~12% off at the top of the range where a period is only ~5 samples.
    /// </summary>
    private float Refine(int tau)
    {
        var before = _difference[tau - 1];
        var center = _difference[tau];
        var after = _difference[tau + 1];
        var curvature = before - 2f * center + after;
        if (curvature <= 0f)
        {
            return 0f;
        }

        var parabolic = Math.Clamp((before - after) / (2f * curvature), -0.5f, 0.5f);
        var offset = parabolic;
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var w = 2f * MathF.PI / (tau + offset);
            var corrected = MathF.Atan(2f * parabolic * MathF.Tan(w / 2f)) / w;
            if (float.IsNaN(corrected) || MathF.Abs(corrected) > 1f)
            {
                return parabolic;
            }

            offset = corrected;
        }

        return Math.Clamp(offset, -0.5f, 0.5f);
    }
}
