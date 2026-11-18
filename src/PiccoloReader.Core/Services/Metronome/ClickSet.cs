namespace PiccoloReader.Core.Services.Metronome;

/// <summary>The three PCM16 click waveforms for one sample rate.</summary>
public sealed class ClickSet
{
    public const double NormalFrequencyHz = 1600;
    public const double AccentFrequencyHz = 2400;
    private const double NormalAmplitude = 0.95;
    private const double AccentAmplitude = 1.0;
    private const double SubdivisionAmplitude = 0.5;

    public ClickSet(int sampleRate)
    {
        Normal = ClickSynth.Create(sampleRate, NormalFrequencyHz, NormalAmplitude);
        Accent = ClickSynth.Create(sampleRate, AccentFrequencyHz, AccentAmplitude);
        Subdivision = ClickSynth.Create(sampleRate, NormalFrequencyHz, SubdivisionAmplitude);
    }

    /// <summary>Custom waveforms, mainly for tests.</summary>
    public ClickSet(short[] normal, short[] accent, short[] subdivision)
    {
        Normal = normal;
        Accent = accent;
        Subdivision = subdivision;
    }

    public short[] Normal { get; }

    public short[] Accent { get; }

    public short[] Subdivision { get; }
}
