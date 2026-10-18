namespace PiccoloReader.Core.Services.Tuner;

public enum TunerBand
{
    None,
    Low,
    High
}

/// <summary>
/// Picks, once per hop, the result of the low band (25 to 500 Hz) or of the high
/// band (400 to 4500 Hz).
/// <list type="bullet">
/// <item>Only one band has a pitch: that one.</item>
/// <item>The high pitch is about an octave or more above the low one (<see cref="ConflictRatio"/>):
/// one band holds a harmonic or a sub-octave of the other. The low band, whose
/// resolution is coarse, may have locked on several periods of a high note, and the
/// high band may have caught a harmonic of a low note. Low wins only when it is
/// confident and clearly clearer than the high band; once chosen it stays unless
/// the high band is clearer by <see cref="SwitchClarityMargin"/>.</item>
/// <item>Otherwise below <see cref="LowOnlyBelowHz"/> the low band wins, above
/// <see cref="HighOnlyAboveHz"/> the high band wins, and in the overlap the previous
/// choice is kept unless the other band is clearer by <see cref="SwitchClarityMargin"/>.</item>
/// </list>
/// </summary>
public sealed class BandSelector
{
    public const float LowOnlyBelowHz = 350f;
    public const float HighOnlyAboveHz = 500f;
    public const float SwitchClarityMargin = 0.05f;
    public const float ConfidentLowClarity = 0.9f;
    public const float ConflictClarityMargin = 0.02f;
    public const float ConflictRatio = 1.9f;

    private TunerBand _previous;

    public TunerBand Previous => _previous;

    public void Reset() => _previous = TunerBand.None;

    public TunerBand Select(in PitchEstimate low, in PitchEstimate high)
    {
        TunerBand chosen;
        if (!low.HasPitch && !high.HasPitch)
        {
            return TunerBand.None;
        }

        if (!high.HasPitch)
        {
            chosen = TunerBand.Low;
        }
        else if (!low.HasPitch)
        {
            chosen = TunerBand.High;
        }
        else if (high.Hz >= low.Hz * ConflictRatio)
        {
            chosen = _previous == TunerBand.Low
                ? (high.Clarity >= low.Clarity + SwitchClarityMargin ? TunerBand.High : TunerBand.Low)
                : (low.Clarity >= ConfidentLowClarity && low.Clarity > high.Clarity + ConflictClarityMargin ? TunerBand.Low : TunerBand.High);
        }
        else if (low.Hz < LowOnlyBelowHz)
        {
            chosen = TunerBand.Low;
        }
        else if (high.Hz > HighOnlyAboveHz)
        {
            chosen = TunerBand.High;
        }
        else if (_previous == TunerBand.Low)
        {
            chosen = high.Clarity >= low.Clarity + SwitchClarityMargin ? TunerBand.High : TunerBand.Low;
        }
        else if (_previous == TunerBand.High)
        {
            chosen = low.Clarity >= high.Clarity + SwitchClarityMargin ? TunerBand.Low : TunerBand.High;
        }
        else
        {
            chosen = low.Clarity > high.Clarity ? TunerBand.Low : TunerBand.High;
        }

        _previous = chosen;
        return chosen;
    }
}
