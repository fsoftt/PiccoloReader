using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class MusicIconCatalogTests
{
    [Fact]
    public void Categories_HasThirteenCategoriesWithExpectedNames()
    {
        var names = MusicIconCatalog.Categories.Select(c => c.Name).ToList();

        Assert.Equal(new[]
        {
            "Dynamics", "Articulations", "Fermata & Breath", "Hairpins",
            "Ornaments", "Bowing & Plucking", "Pedal Marks", "Repeats & Navigation",
            "More Dynamics", "Accidentals", "Tremolo & Glissando", "Notes", "Rests",
        }, names);
    }

    [Fact]
    public void Categories_AllIconsHaveUniqueKeys()
    {
        var keys = MusicIconCatalog.Categories.SelectMany(c => c.Icons).Select(i => i.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void FindByKey_KnownKey_ReturnsIcon()
    {
        var icon = MusicIconCatalog.FindByKey("dynamicForte");

        Assert.NotNull(icon);
        Assert.Equal("f", icon!.DisplayName);
        Assert.Equal(0xE522, icon.Codepoint);
    }

    [Fact]
    public void FindByKey_UnknownKey_ReturnsNull()
    {
        Assert.Null(MusicIconCatalog.FindByKey("notARealIcon"));
    }

    [Fact]
    public void FindByKey_NewOrnamentIcon_ReturnsIcon()
    {
        var icon = MusicIconCatalog.FindByKey("ornamentTrill");

        Assert.NotNull(icon);
        Assert.Equal(0xE566, icon!.Codepoint);
    }

    [Fact]
    public void FindByKey_NewMezzoPianoIcon_ReturnsIcon()
    {
        var icon = MusicIconCatalog.FindByKey("dynamicMP");

        Assert.NotNull(icon);
        Assert.Equal("mp", icon!.DisplayName);
        Assert.Equal(0xE52C, icon.Codepoint);
    }

    [Fact]
    public void Categories_NotesAndRestsAreLast()
    {
        var cats = MusicIconCatalog.Categories;

        Assert.Equal("Notes", cats[^2].Name);
        Assert.Equal("Rests", cats[^1].Name);
        Assert.Equal(15, cats[^2].Icons.Count);
        Assert.Equal(6, cats[^1].Icons.Count);
    }

    [Theory]
    [InlineData("noteWhole", 0xE1D2)]
    [InlineData("noteHalfUp", 0xE1D3)]
    [InlineData("noteQuarterUp", 0xE1D5)]
    [InlineData("noteQuarterDown", 0xE1D6)]
    [InlineData("note8thUp", 0xE1D7)]
    [InlineData("note32ndDown", 0xE1DC)]
    [InlineData("augmentationDot", 0xE1E7)]
    [InlineData("graceNoteAcciaccaturaStemDown", 0xE561)]
    [InlineData("graceNoteAppoggiaturaStemUp", 0xE562)]
    [InlineData("graceNoteAppoggiaturaStemDown", 0xE563)]
    [InlineData("restWhole", 0xE4E3)]
    [InlineData("restHalf", 0xE4E4)]
    [InlineData("restQuarter", 0xE4E5)]
    [InlineData("rest8th", 0xE4E6)]
    [InlineData("rest16th", 0xE4E7)]
    [InlineData("rest32nd", 0xE4E8)]
    public void FindByKey_NotesAndRests_ReturnExpectedCodepoint(string key, int codepoint)
    {
        var icon = MusicIconCatalog.FindByKey(key);

        Assert.NotNull(icon);
        Assert.Equal(codepoint, icon!.Codepoint);
        Assert.False(string.IsNullOrWhiteSpace(icon.DisplayName));
    }

    [Fact]
    public void Categories_AllIconsHaveUniqueCodepointsWithinCategory()
    {
        foreach (var category in MusicIconCatalog.Categories)
        {
            var cps = category.Icons.Select(i => i.Codepoint).ToList();
            Assert.Equal(cps.Count, cps.Distinct().Count());
        }
    }
}
