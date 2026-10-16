using System.Globalization;

namespace PiccoloReader.Core.Services.Tuner;

/// <summary>Nearest note of a frequency. Octave uses scientific pitch notation (A4 = MIDI 69).</summary>
public readonly record struct NoteReading(int MidiNote, float Cents, int PitchClass, int Octave);

public static class NoteMapper
{
    public static NoteReading FromHz(float hz, int a4Hz, int transposeSemitones = 0)
    {
        var midi = 69f + 12f * MathF.Log2(hz / a4Hz) + transposeSemitones;
        var nearest = (int)MathF.Round(midi);
        var cents = (midi - nearest) * 100f;
        var pitchClass = ((nearest % 12) + 12) % 12;
        var octave = (int)MathF.Floor(nearest / 12f) - 1;
        return new NoteReading(nearest, cents, pitchClass, octave);
    }
}

public static class NoteNames
{
    private static readonly string[] Letters = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] Solfege = ["Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"];

    /// <summary>Resolves <see cref="NoteNaming.Auto"/> to a concrete naming: Spanish cultures use solfege.</summary>
    public static NoteNaming Resolve(NoteNaming naming, CultureInfo culture)
    {
        if (naming != NoteNaming.Auto)
        {
            return naming;
        }

        return string.Equals(culture.TwoLetterISOLanguageName, "es", StringComparison.OrdinalIgnoreCase)
            ? NoteNaming.Solfege
            : NoteNaming.Letter;
    }

    /// <summary>Cached name for a pitch class (0 = C). <see cref="NoteNaming.Auto"/> is treated as letters; resolve it first.</summary>
    public static string Get(int pitchClass, NoteNaming resolved)
    {
        var index = ((pitchClass % 12) + 12) % 12;
        return resolved == NoteNaming.Solfege ? Solfege[index] : Letters[index];
    }
}
