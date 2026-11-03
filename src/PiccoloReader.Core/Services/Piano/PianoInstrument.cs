using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Services.Piano;

public enum PianoFamily
{
    Keyboards,
    Woodwinds,
    Brass,
    Strings,
    Voices
}

/// <summary>
/// One playable sound. <see cref="LowNote"/> and <see cref="HighNote"/> are the sounding range
/// of the real instrument (MIDI); keys outside it stay playable but are drawn muted.
/// </summary>
public sealed record PianoInstrument(
    string Id,
    int GmProgram,
    string NameKey,
    PianoFamily Family,
    int LowNote,
    int HighNote,
    float Gain)
{
    public string Name => AppStrings.GetString(NameKey);

    public bool IsInRange(int note) => note >= LowNote && note <= HighNote;
}

public static class PianoInstruments
{
    private static readonly PianoInstrument[] Instruments =
    [
        new("grand", 0, "PianoGrand", PianoFamily.Keyboards, 21, 108, 5.0f),
        new("electric", 4, "PianoElectric", PianoFamily.Keyboards, 28, 100, 1.0f),
        new("organ", 19, "PianoOrgan", PianoFamily.Keyboards, 36, 96, 1.39f),
        new("piccolo", 72, "PianoPiccolo", PianoFamily.Woodwinds, 74, 108, 0.87f),
        new("flute", 73, "PianoFlute", PianoFamily.Woodwinds, 60, 98, 1.0f),
        new("oboe", 68, "PianoOboe", PianoFamily.Woodwinds, 58, 91, 0.94f),
        new("clarinet", 71, "PianoClarinet", PianoFamily.Woodwinds, 50, 96, 0.97f),
        new("altosax", 65, "PianoAltoSax", PianoFamily.Woodwinds, 49, 80, 0.84f),
        new("tenorsax", 66, "PianoTenorSax", PianoFamily.Woodwinds, 44, 76, 0.94f),
        new("trumpet", 56, "PianoTrumpet", PianoFamily.Brass, 52, 84, 1.28f),
        new("trombone", 57, "PianoTrombone", PianoFamily.Brass, 40, 77, 0.42f),
        new("horn", 60, "PianoFrenchHorn", PianoFamily.Brass, 35, 77, 0.81f),
        new("violin", 40, "PianoViolin", PianoFamily.Strings, 55, 103, 1.13f),
        new("cello", 42, "PianoCello", PianoFamily.Strings, 36, 81, 0.87f),
        new("strings", 48, "PianoStrings", PianoFamily.Strings, 28, 100, 1.27f),
        new("guitar", 24, "PianoNylonGuitar", PianoFamily.Strings, 40, 88, 2.18f),
        new("choir", 52, "PianoChoir", PianoFamily.Voices, 40, 84, 1.44f)
    ];

    /// <summary>All instruments, grouped by family in display order.</summary>
    public static IReadOnlyList<PianoInstrument> All => Instruments;

    public static PianoInstrument Default => Instruments[0];

    /// <summary>The instrument with this id, or <see cref="Default"/> when unknown (e.g. a removed one).</summary>
    public static PianoInstrument ById(string? id) =>
        Array.Find(Instruments, instrument => instrument.Id == id) ?? Default;

    public static string FamilyName(PianoFamily family) => AppStrings.GetString("PianoFamily" + family);
}
