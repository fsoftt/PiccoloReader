namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Fourth-order low-pass (two Butterworth-like biquad sections) for the low band.
/// At about 3 kHz the harmonics of a note above 250 Hz sit a couple of samples per
/// cycle apart, so the lag of one period falls between two samples and YIN prefers
/// a lag of two or three periods (an octave or more too low). Removing everything
/// above the 500 Hz ceiling of the band leaves nearly a sinusoid, whose period is
/// found exactly. Allocation-free; processes in place.
/// </summary>
internal sealed class LowBandFilter
{
    private const double Q = 0.70710678;

    private readonly float _b0;
    private readonly float _b1;
    private readonly float _a1;
    private readonly float _a2;
    private float _z1a;
    private float _z2a;
    private float _z1b;
    private float _z2b;

    public LowBandFilter(float sampleRate, float cutoffHz)
    {
        var w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
        var alpha = Math.Sin(w0) / (2.0 * Q);
        var cos = Math.Cos(w0);
        var a0 = 1.0 + alpha;
        _b0 = (float)(((1.0 - cos) / 2.0) / a0);
        _b1 = (float)((1.0 - cos) / a0);
        _a1 = (float)(-2.0 * cos / a0);
        _a2 = (float)((1.0 - alpha) / a0);
    }

    public void Reset()
    {
        _z1a = 0f;
        _z2a = 0f;
        _z1b = 0f;
        _z2b = 0f;
    }

    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var y = (_b0 * x) + _z1a;
            _z1a = (_b1 * x) - (_a1 * y) + _z2a;
            _z2a = (_b0 * x) - (_a2 * y);

            var z = (_b0 * y) + _z1b;
            _z1b = (_b1 * y) - (_a1 * z) + _z2b;
            _z2b = (_b0 * y) - (_a2 * z);
            samples[i] = z;
        }
    }
}
