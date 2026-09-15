using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class MusicIconCatalogTests
{
    [Fact]
    public void Categories_HasFourCategoriesWithExpectedNames()
    {
        var names = MusicIconCatalog.Categories.Select(c => c.Name).ToList();

        Assert.Equal(new[] { "Dynamics", "Articulations", "Fermata & Breath", "Hairpins" }, names);
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
}
