using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerTranspositionTests
{
    [Theory]
    [InlineData(TransposeKey.C, -1, -12)] // piccolo
    [InlineData(TransposeKey.Bb, 0, 2)] // Bb clarinet, trumpet
    [InlineData(TransposeKey.Bb, 1, 14)] // tenor sax
    [InlineData(TransposeKey.Eb, 0, 9)] // alto sax
    [InlineData(TransposeKey.Eb, 1, 21)] // baritone sax
    [InlineData(TransposeKey.Eb, -1, -3)] // Eb clarinet
    [InlineData(TransposeKey.F, 0, 7)] // horn
    [InlineData(TransposeKey.Bb, 1, 14)] // brass band Bb tuba, treble clef
    [InlineData(TransposeKey.C, 0, 0)] // orchestral tuba
    [InlineData(TransposeKey.A, 0, 3)]
    [InlineData(TransposeKey.G, 0, 5)]
    [InlineData(TransposeKey.G, 2, 29)]
    public void TransposeSemitones_IsKeyIntervalPlusOctaves(TransposeKey key, int octave, int expected)
    {
        var settings = new TunerSettings { Key = key, OctaveShift = octave };

        Assert.Equal(expected, settings.TransposeSemitones);
    }

    [Fact]
    public void Default_IsConcertPitch()
    {
        Assert.Equal(0, new TunerSettings().TransposeSemitones);
    }

    [Theory]
    [InlineData(-5, -2)]
    [InlineData(3, 2)]
    public void OctaveShift_IsClamped(int value, int expected)
    {
        Assert.Equal(expected, new TunerSettings { OctaveShift = value }.OctaveShift);
    }

    [Fact]
    public void AltoSax_ConcertA4_IsWrittenF5Sharp_WithSameCents()
    {
        var settings = new TunerSettings { Key = TransposeKey.Eb };

        var note = NoteMapper.FromHz(440f * MathF.Pow(2f, 7f / 1200f), 440, settings.TransposeSemitones);

        Assert.Equal(6, note.PitchClass); // F#
        Assert.Equal(5, note.Octave);
        Assert.InRange(note.Cents, 6.9f, 7.1f);
    }

    [Fact]
    public void BbTuba_LowestNote_IsWrittenInTrebleClef()
    {
        var settings = new TunerSettings { Key = TransposeKey.Bb, OctaveShift = 1 };

        // Bb0 (29.14 Hz) is written C2 for a brass-band Bb tuba.
        var note = NoteMapper.FromHz(29.135f, 440, settings.TransposeSemitones);

        Assert.Equal(0, note.PitchClass);
        Assert.Equal(2, note.Octave);
    }

    [Theory]
    [InlineData(true, TransposeKey.C, -1)]
    [InlineData(false, TransposeKey.C, 0)]
    public void LegacyWrittenFlag_MapsToCAndOctave(bool legacy, TransposeKey key, int octave)
    {
        var (migratedKey, migratedOctave) = TunerSettings.FromLegacyWritten(legacy);

        Assert.Equal(key, migratedKey);
        Assert.Equal(octave, migratedOctave);
        Assert.Equal(legacy ? -12 : 0, new TunerSettings { Key = migratedKey, OctaveShift = migratedOctave }.TransposeSemitones);
    }
}
