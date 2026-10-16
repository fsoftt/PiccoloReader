namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// YIN pitch detector tuned for a cheap per-frame cost: an RMS gate skips silent
/// windows, the difference function is only computed up to the first dip under
/// the threshold, and every buffer is preallocated.
/// </summary>
public sealed class YinPitchDetector
{
    private readonly float _sampleRate;
    private readonly int _windowSize;
    private readonly int _integrationLength;
    private readonly int _tauMin;
    private readonly int _tauMax;
    private readonly float _threshold;
    private readonly float _rmsGate;
    private readonly float[] _centered;
    private readonly float[] _difference;
    private readonly float[] _cmndf;

    public YinPitchDetector(float sampleRate, int windowSize = 1024, float minHz = 250f, float maxHz = 4500f, float threshold = 0.13f, float rmsGate = 0.005f)
    {
        _sampleRate = sampleRate;
        _windowSize = windowSize;
        _threshold = threshold;
        _rmsGate = rmsGate;
        _tauMax = (int)(sampleRate / minHz);
        _tauMin = Math.Max(2, (int)(sampleRate / maxHz));
        _integrationLength = windowSize - _tauMax;
        if (_integrationLength <= 0 || _tauMin >= _tauMax)
        {
            throw new ArgumentException("Window too short for the requested pitch range.");
        }

        _centered = new float[windowSize];
        _difference = new float[_tauMax + 2];
        _cmndf = new float[_tauMax + 2];
    }

    public int WindowSize => _windowSize;

    public PitchEstimate Detect(ReadOnlySpan<float> window)
    {
        if (window.Length < _windowSize)
        {
            return PitchEstimate.None(0f);
        }

        // Remove DC so it counts neither toward the RMS gate nor the difference function.
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

        var rms = MathF.Sqrt(energy / _windowSize);
        if (rms < _rmsGate)
        {
            return PitchEstimate.None(rms);
        }

        var d = _difference;
        var cm = _cmndf;
        var running = 0f;
        var chosen = 0;

        for (var tau = 1; tau <= _tauMax; tau++)
        {
            ComputeDifference(tau);
            running += d[tau];
            cm[tau] = running > 0f ? d[tau] * tau / running : 1f;

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

        if (chosen == 0)
        {
            return PitchEstimate.None(rms);
        }

        var offset = chosen < _tauMax ? Refine(chosen) : 0f;
        var hz = _sampleRate / (chosen + offset);
        return new PitchEstimate(hz, 1f - cm[chosen], rms, true);
    }

    private void ComputeDifference(int tau)
    {
        var x = _centered;
        var acc = 0f;
        var lagged = x.AsSpan(tau, _integrationLength);
        var head = x.AsSpan(0, _integrationLength);
        for (var j = 0; j < head.Length; j++)
        {
            var diff = head[j] - lagged[j];
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

        var parabolic = (before - after) / (2f * curvature);
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

        return offset;
    }
}
