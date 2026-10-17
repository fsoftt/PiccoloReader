using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class HairpinGeometryTests
{
    [Fact]
    public void IsHairpin_OnlyCrescendoAndDecrescendoKeys()
    {
        Assert.True(HairpinGeometry.IsHairpin("dynamicCrescendoHairpin"));
        Assert.True(HairpinGeometry.IsHairpin("dynamicDiminuendoHairpin"));
        Assert.False(HairpinGeometry.IsHairpin("dynamicForte"));
        Assert.False(HairpinGeometry.IsHairpin(null));
    }

    [Fact]
    public void CatalogHairpinKeys_AreHairpins()
    {
        var hairpins = MusicIconCatalog.Categories
            .SelectMany(c => c.Icons)
            .Where(i => i.Key.EndsWith("Hairpin"))
            .ToList();

        Assert.Equal(2, hairpins.Count);

        Assert.All(hairpins, i => Assert.True(HairpinGeometry.IsHairpin(i.Key)));
    }

    [Fact]
    public void ClampSize_LongHairpin_KeepsLengthAndCapsOpening()
    {
        // A4-ish portrait page: height / width = 1.414.
        var (width, height) = HairpinGeometry.ClampSize(0.6, 0.5, 1.414);

        Assert.Equal(0.6, width);
        Assert.Equal(HairpinGeometry.MaxOpening / 1.414, height, 6);
    }

    [Fact]
    public void ClampSize_TooShortOrTooSmall_RaisedToMinimums()
    {
        var (width, height) = HairpinGeometry.ClampSize(0.01, 0.001, 1.414);

        Assert.Equal(HairpinGeometry.MinWidth, width);
        Assert.Equal(HairpinGeometry.MinOpening / 1.414, height, 6);
    }

    [Fact]
    public void ClampSize_WiderThanPage_CappedAtPageWidth()
    {
        var (width, _) = HairpinGeometry.ClampSize(3, 0.03, 1.414);

        Assert.Equal(1.0, width);
    }

    [Fact]
    public void ClampSize_DefaultPlacedSize_IsUnchanged()
    {
        // PlaceIcon: width 0.12, height = width / 2.78 / pageAspect.
        const double aspect = 1.414;
        var defaultHeight = 0.12 / 2.78 / aspect;

        var (width, height) = HairpinGeometry.ClampSize(0.12, defaultHeight, aspect);

        Assert.Equal(0.12, width, 6);
        Assert.Equal(defaultHeight, height, 6);
    }

    [Fact]
    public void Lines_CrescendoOpensRight_DecrescendoClosesRight()
    {
        var cresc = HairpinGeometry.Lines(HairpinGeometry.CrescendoKey);
        var decresc = HairpinGeometry.Lines(HairpinGeometry.DecrescendoKey);

        Assert.All(cresc, l => Assert.Equal((0.0, 0.5), (l.X1, l.Y1)));
        Assert.All(decresc, l => Assert.Equal((1.0, 0.5), (l.X2, l.Y2)));
    }

    [Fact]
    public void ClampBox_NearRightEdge_CapsLengthAtSpaceRight()
    {
        var (x, width, _) = HairpinGeometry.ClampBox(0.6, 0.9, 0.03, 1.414);

        Assert.Equal(0.6, x);
        Assert.Equal(0.4, width, 6);
    }

    [Fact]
    public void ClampBox_NoRoomForMinimum_ShiftsLeft()
    {
        var (x, width, _) = HairpinGeometry.ClampBox(0.98, 0.5, 0.03, 1.414, 0.9);

        Assert.Equal(HairpinGeometry.MinWidth, width);
        Assert.Equal(0.9 - HairpinGeometry.MinWidth, x, 6);
    }
}
