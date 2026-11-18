namespace PiccoloReader.Core.Services.Metronome;

public static class ClickSynth
{
    // Must stay below the shortest click interval (250 BPM x 4 subdivisions = 60 ms),
    // otherwise the scheduler cuts the ringing click when the next one starts.
    public const double DefaultDurationMs = 50;

    private const double AttackMs = 1;
    private const double DecayTimeConstantMs = 11;
    private const double FadeOutMs = 5;
    private const double SecondHarmonic = 0.5;
    private const double ThirdHarmonic = 0.25;
    private const double SaturationDrive = 2;

    /// <summary>
    /// Click with a fundamental plus 2nd and 3rd harmonics, a 1 ms attack and an exponential decay,
    /// soft-saturated for loudness, mono PCM16. Amplitude is a 0..1 fraction of full scale and is the exact peak.
    /// </summary>
    public static short[] Create(int sampleRate, double frequencyHz, double amplitude, double durationMs = DefaultDurationMs)
    {
        var length = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
        var peak = Math.Clamp(amplitude, 0, 1) * short.MaxValue;
        var angular = 2 * Math.PI * frequencyHz / sampleRate;
        var attack = Math.Max(1, sampleRate * AttackMs / 1000.0);
        var tau = sampleRate * DecayTimeConstantMs / 1000.0;
        var fade = Math.Min(length, Math.Max(1, (int)(sampleRate * FadeOutMs / 1000.0)));
        var norm = Math.Tanh(SaturationDrive);

        var raw = new double[length];
        var max = 0.0;
        for (var i = 0; i < length; i++)
        {
            var tone = Math.Sin(angular * i)
                + SecondHarmonic * Math.Sin(2 * angular * i)
                + ThirdHarmonic * Math.Sin(3 * angular * i);
            var env = Math.Min(1.0, i / attack) * Math.Exp(-i / tau);
            if (i >= length - fade)
            {
                env *= 0.5 * (1 + Math.Cos(Math.PI * (i - (length - fade)) / fade));
            }

            // Normalize the tone to ~1 before saturating so the drive is independent of the partial mix.
            var v = Math.Tanh(SaturationDrive * env * tone / (1 + SecondHarmonic + ThirdHarmonic)) / norm;
            raw[i] = v;
            max = Math.Max(max, Math.Abs(v));
        }

        var samples = new short[length];
        if (max <= 0)
        {
            return samples;
        }

        var scale = peak / max;
        for (var i = 0; i < length; i++)
        {
            samples[i] = (short)Math.Clamp(Math.Round(raw[i] * scale), short.MinValue + 1, short.MaxValue);
        }

        return samples;
    }
}
