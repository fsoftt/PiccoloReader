using PiccoloReader.Core.Services.Stylus;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class ContinuousStylusGeometryTests
{
    [Fact]
    public void ToPagePoint_PageFillsItem_MapsLinearly()
    {
        var p = ContinuousStylusGeometry.ToPagePoint(50, 100, 0, 0, 100, 200, 2, PageCrop.Full);

        Assert.NotNull(p);
        Assert.Equal(0.5, p.X, 6);
        Assert.Equal(0.5, p.Y, 6);
    }

    [Fact]
    public void ToPagePoint_ItemScrolledDown_UsesItemOrigin()
    {
        var p = ContinuousStylusGeometry.ToPagePoint(25, 450, 0, 300, 100, 200, 2, PageCrop.Full);

        Assert.NotNull(p);
        Assert.Equal(0.25, p.X, 6);
        Assert.Equal(0.75, p.Y, 6);
    }

    [Fact]
    public void ToPagePoint_PageNarrowerThanItem_IsLetterboxed()
    {
        // Page of aspect 2 in a 100x100 box is 50 wide, centered.
        var p = ContinuousStylusGeometry.ToPagePoint(25, 50, 0, 0, 100, 100, 2, PageCrop.Full);

        Assert.NotNull(p);
        Assert.Equal(0, p.X, 6);
        Assert.Equal(0.5, p.Y, 6);
    }

    [Fact]
    public void ToPagePoint_OutsidePage_GivesValuesBeyondRange()
    {
        var p = ContinuousStylusGeometry.ToPagePoint(150, -20, 0, 0, 100, 200, 2, PageCrop.Full);

        Assert.NotNull(p);
        Assert.True(p.X > 1);
        Assert.True(p.Y < 0);
    }

    [Fact]
    public void ToPagePoint_EmptyItem_ReturnsNull()
    {
        Assert.Null(ContinuousStylusGeometry.ToPagePoint(1, 1, 0, 0, 0, 100, 2, PageCrop.Full));
    }
}
