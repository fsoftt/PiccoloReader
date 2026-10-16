namespace PiccoloReader.Core.Services.Metronome;

public static class ClickSynth
{
    public const double DefaultDurationMs = 25;

    // exp(-DecayConstants) is the envelope level at the end of the click.
    private const double DecayConstants = 6;

    /// <summary>Sine burst with an exponential decay, mono PCM16. Amplitude is a 0..1 fraction of full scale.</summary>
    public static short[] Create(int sampleRate, double frequencyHz, double amplitude, double durationMs = DefaultDurationMs)
    {
        var length = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
        var samples = new short[length];
        var peak = Math.Clamp(amplitude, 0, 1) * short.MaxValue;
        var angular = 2 * Math.PI * frequencyHz / sampleRate;
        var decay = DecayConstants / length;

        for (var i = 0; i < length; i++)
        {
            samples[i] = (short)Math.Round(peak * Math.Sin(angular * i) * Math.Exp(-decay * i));
        }

        return samples;
    }
}
