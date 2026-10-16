namespace PiccoloReader.Core.Services.Tuner;

public enum NoteNaming
{
    /// <summary>Solfege for Spanish UI cultures, letters otherwise.</summary>
    Auto,
    Letter,
    Solfege
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

    /// <summary>Piccolo sounds one octave above written pitch.</summary>
    public const int WrittenTransposeSemitones = -12;

    private static readonly int[] PresetValues = [PresetA4Standard, PresetA4Orchestra, PresetA4Bright];

    public static IReadOnlyList<int> Presets => PresetValues;

    private readonly int _a4Hz = DefaultA4Hz;

    public int A4Hz
    {
        get => _a4Hz;
        init => _a4Hz = Math.Clamp(value, MinA4Hz, MaxA4Hz);
    }

    /// <summary>Shows the written note (concert pitch minus an octave) instead of the sounding one.</summary>
    public bool ShowAsWritten { get; init; }

    public NoteNaming NoteNaming { get; init; } = NoteNaming.Auto;

    public int TransposeSemitones => ShowAsWritten ? WrittenTransposeSemitones : 0;
}
