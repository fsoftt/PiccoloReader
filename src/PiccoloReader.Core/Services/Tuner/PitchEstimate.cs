namespace PiccoloReader.Core.Services.Tuner;

/// <summary>Result of analysing one window. Clarity is 1 - CMNDF at the chosen lag.</summary>
public readonly record struct PitchEstimate(float Hz, float Clarity, float Rms, bool HasPitch)
{
    public static PitchEstimate None(float rms) => new(0f, 0f, rms, false);
}
