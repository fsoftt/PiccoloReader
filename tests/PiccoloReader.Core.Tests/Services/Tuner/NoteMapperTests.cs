using System.Globalization;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class NoteMapperTests
{
    [Fact]
    public void A4_IsMidi69WithZeroCents()
    {
        var note = NoteMapper.FromHz(440f, 440);

        Assert.Equal(69, note.MidiNote);
        Assert.Equal(0f, note.Cents, 3);
        Assert.Equal(9, note.PitchClass);
        Assert.Equal(4, note.Octave);
    }

    [Fact]
    public void Cents_SignFollowsPitch()
    {
        var sharp = NoteMapper.FromHz((float)(440 * Math.Pow(2, 20 / 1200.0)), 440);
        var flat = NoteMapper.FromHz((float)(440 * Math.Pow(2, -20 / 1200.0)), 440);

        Assert.Equal(69, sharp.MidiNote);
        Assert.Equal(20f, sharp.Cents, 1);
        Assert.Equal(69, flat.MidiNote);
        Assert.Equal(-20f, flat.Cents, 1);
    }

    [Fact]
    public void A4442_Maps442ToA4()
    {
        var note = NoteMapper.FromHz(442f, 442);

        Assert.Equal(69, note.MidiNote);
        Assert.Equal(0f, note.Cents, 3);
        Assert.True(NoteMapper.FromHz(442f, 440).Cents > 0);
    }

    [Fact]
    public void CentsStayWithinHalfSemitone()
    {
        for (var hz = 500f; hz < 4300f; hz *= 1.003f)
        {
            Assert.InRange(NoteMapper.FromHz(hz, 440).Cents, -50.01f, 50.01f);
        }
    }

    [Fact]
    public void PiccoloRange_EndsAreD5AndC8()
    {
        var low = NoteMapper.FromHz(587.33f, 440);
        var high = NoteMapper.FromHz(4186.01f, 440);

        Assert.Equal((2, 5), (low.PitchClass, low.Octave));
        Assert.Equal((0, 8), (high.PitchClass, high.Octave));
    }

    [Fact]
    public void ShowAsWritten_TransposesDownAnOctave()
    {
        var settings = new TunerSettings { ShowAsWritten = true };

        var note = NoteMapper.FromHz(880f, 440, settings.TransposeSemitones);

        Assert.Equal(69, note.MidiNote);
        Assert.Equal(9, note.PitchClass);
        Assert.Equal(4, note.Octave);
        Assert.Equal(0, new TunerSettings().TransposeSemitones);
    }

    [Fact]
    public void Names_LetterAndSolfege()
    {
        string[] letters = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        string[] solfege = ["Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"];
        for (var pc = 0; pc < 12; pc++)
        {
            Assert.Equal(letters[pc], NoteNames.Get(pc, NoteNaming.Letter));
            Assert.Equal(solfege[pc], NoteNames.Get(pc, NoteNaming.Solfege));
        }

        Assert.Same(NoteNames.Get(3, NoteNaming.Letter), NoteNames.Get(3, NoteNaming.Letter));
    }

    [Theory]
    [InlineData("es", NoteNaming.Solfege)]
    [InlineData("es-MX", NoteNaming.Solfege)]
    [InlineData("en", NoteNaming.Letter)]
    [InlineData("fr-FR", NoteNaming.Letter)]
    public void Auto_ResolvesFromCulture(string culture, NoteNaming expected)
    {
        Assert.Equal(expected, NoteNames.Resolve(NoteNaming.Auto, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Explicit_NamingIgnoresCulture()
    {
        var es = CultureInfo.GetCultureInfo("es");

        Assert.Equal(NoteNaming.Letter, NoteNames.Resolve(NoteNaming.Letter, es));
        Assert.Equal(NoteNaming.Solfege, NoteNames.Resolve(NoteNaming.Solfege, CultureInfo.GetCultureInfo("en")));
    }
}
