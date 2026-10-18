namespace PiccoloReader.Core.Services.Tuner;

public enum NoteNaming
{
    /// <summary>Solfege for Spanish UI cultures, letters otherwise.</summary>
    Auto,
    Letter,
    Solfege
}

/// <summary>Key of the instrument whose written pitch is shown.</summary>
public enum TransposeKey
{
    C,
    Bb,
    A,
    Eb,
    F,
    G
}

/// <summary>
/// Immutable tuner configuration. A4Hz clamps to its valid range, so a
/// snapshot can be handed to the audio thread without checks.
/// </summary>
public sealed record TunerSettings
{
    public const int MinA4Hz = 430;
    public const int MaxA4Hz = 450;
    public const int DefaultA4Hz = 440;

    public const int PresetA4Standard = 440;
    public const int PresetA4Orchestra = 442;
    public const int PresetA4Bright = 443;

    public const int MinOctaveShift = -2;
    public const int MaxOctaveShift = 2;

    private static readonly int[] PresetValues = [PresetA4Standard, PresetA4Orchestra, PresetA4Bright];

    public static IReadOnlyList<int> Presets => PresetValues;

    private readonly int _a4Hz = DefaultA4Hz;
    private readonly int _octaveShift;

    public int A4Hz
    {
        get => _a4Hz;
        init => _a4Hz = Math.Clamp(value, MinA4Hz, MaxA4Hz);
    }

    /// <summary>Key of the instrument: the written note is the sounding one plus the key interval.</summary>
    public TransposeKey Key { get; init; }

    /// <summary>Octaves added to the written note, from -2 to +2 (the piccolo is written an octave below it sounds).</summary>
    public int OctaveShift
    {
        get => _octaveShift;
        init => _octaveShift = Math.Clamp(value, MinOctaveShift, MaxOctaveShift);
    }

    public NoteNaming NoteNaming { get; init; } = NoteNaming.Auto;

    /// <summary>Semitones from the sounding (concert) note to the written one.</summary>
    public int TransposeSemitones => KeyInterval(Key) + (12 * OctaveShift);

    /// <summary>Semitones a note written for an instrument in this key is above the sounding one.</summary>
    public static int KeyInterval(TransposeKey key) => key switch
    {
        TransposeKey.Bb => 2,
        TransposeKey.A => 3,
        TransposeKey.Eb => 9,
        TransposeKey.F => 7,
        TransposeKey.G => 5,
        _ => 0
    };

    /// <summary>Settings equivalent to the former on/off written-pitch option.</summary>
    public static (TransposeKey Key, int OctaveShift) FromLegacyWritten(bool showAsWritten) =>
        (TransposeKey.C, showAsWritten ? -1 : 0);
}
