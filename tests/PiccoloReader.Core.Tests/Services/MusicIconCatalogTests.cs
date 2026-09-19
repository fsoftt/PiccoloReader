using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class MusicIconCatalogTests
{
    [Fact]
    public void Categories_HasElevenCategoriesWithExpectedNames()
    {
        var names = MusicIconCatalog.Categories.Select(c => c.Name).ToList();

        Assert.Equal(new[]
        {
            "Dynamics", "Articulations", "Fermata & Breath", "Hairpins",
            "Ornaments", "Bowing & Plucking", "Pedal Marks", "Repeats & Navigation",
            "More Dynamics", "Accidentals", "Tremolo & Glissando",
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
}
