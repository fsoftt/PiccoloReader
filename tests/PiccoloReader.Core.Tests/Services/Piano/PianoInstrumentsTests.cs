using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests.Services.Piano;

public class PianoInstrumentsTests
{
    [Fact]
    public void All_HasSeventeenInstrumentsWithUniqueIds()
    {
        Assert.Equal(17, PianoInstruments.All.Count);
        Assert.Equal(17, PianoInstruments.All.Select(i => i.Id).Distinct().Count());
        Assert.Equal(17, PianoInstruments.All.Select(i => i.GmProgram).Distinct().Count());
    }

    [Fact]
    public void All_RangesAreValidAndInsideTheKeyboard()
    {
        foreach (var instrument in PianoInstruments.All)
        {
            Assert.InRange(instrument.LowNote, PianoKeyboardLayout.LowestNote, PianoKeyboardLayout.HighestNote);
            Assert.InRange(instrument.HighNote, PianoKeyboardLayout.LowestNote, PianoKeyboardLayout.HighestNote);
            Assert.True(instrument.LowNote < instrument.HighNote, instrument.Id);
            Assert.True(instrument.Gain > 0f, instrument.Id);
        }
    }

    [Fact]
    public void All_IsGroupedByFamilyInDisplayOrder()
    {
        var families = PianoInstruments.All.Select(i => i.Family).ToList();

        Assert.Equal(families.OrderBy(f => f), families);
    }

    [Fact]
    public void All_EveryInstrumentHasANameInBothLanguages()
    {
        foreach (var instrument in PianoInstruments.All)
        {
            Assert.NotEqual(instrument.NameKey, instrument.Name);
        }
    }

    [Fact]
    public void All_EveryGmProgramIsPresentInTheShippedSoundFont()
    {
        var presets = PianoAsset.SoundFont.Presets
            .Where(p => p.BankNumber == 0)
            .Select(p => p.PatchNumber)
            .ToHashSet();

        foreach (var instrument in PianoInstruments.All)
        {
            Assert.Contains(instrument.GmProgram, presets);
        }
    }

    [Fact]
    public void Asset_IsWithinTheSizeBudget()
    {
        Assert.True(new FileInfo(PianoAsset.Path).Length <= 10 * 1024 * 1024);
    }

    [Fact]
    public void ById_UnknownOrNull_FallsBackToTheGrandPiano()
    {
        Assert.Equal("flute", PianoInstruments.ById("flute").Id);
        Assert.Equal("grand", PianoInstruments.ById("missing").Id);
        Assert.Equal("grand", PianoInstruments.ById(null).Id);
    }

    [Fact]
    public void IsInRange_IncludesBothEnds()
    {
        var flute = PianoInstruments.ById("flute");

        Assert.True(flute.IsInRange(flute.LowNote));
        Assert.True(flute.IsInRange(flute.HighNote));
        Assert.False(flute.IsInRange(flute.LowNote - 1));
        Assert.False(flute.IsInRange(flute.HighNote + 1));
    }
}
