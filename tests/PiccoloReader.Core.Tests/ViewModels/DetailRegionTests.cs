using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class DetailRegionTests
{
    [Fact]
    public void VisibleLocalRect_NotZoomed_IsWholeView()
    {
        var rect = DetailRegion.VisibleLocalRect(400, 800, 1, 0, 0);

        Assert.Equal(new RectD(0, 0, 400, 800), rect);
    }

    [Fact]
    public void VisibleLocalRect_ZoomedAndShifted_MapsBackToLocalSpace()
    {
        // 2x zoom, content shifted left by 100 and up by 300 on screen.
        var rect = DetailRegion.VisibleLocalRect(400, 800, 2, -100, -300);

        Assert.Equal(50, rect.X, 6);
        Assert.Equal(150, rect.Y, 6);
        Assert.Equal(200, rect.Width, 6);
        Assert.Equal(400, rect.Height, 6);
    }

    [Fact]
    public void AspectFit_WideContainer_LetterboxesHorizontally()
    {
        Assert.Equal(new RectD(50, 0, 100, 100), DetailRegion.AspectFit(200, 100, 1.0));
    }

    [Fact]
    public void AspectFit_TallContainer_LetterboxesVertically()
    {
        Assert.Equal(new RectD(0, 50, 100, 100), DetailRegion.AspectFit(100, 200, 1.0));
    }

    [Fact]
    public void AspectFit_UnknownAspect_FillsContainer()
    {
        Assert.Equal(new RectD(0, 0, 100, 200), DetailRegion.AspectFit(100, 200, 0));
    }

    [Fact]
    public void Compute_ZoomedIntoTopLeftQuarter_NormalizesRegion()
    {
        var image = new RectD(0, 0, 400, 800);
        var visible = DetailRegion.VisibleLocalRect(400, 800, 2, 0, 0);

        var region = DetailRegion.Compute(visible, image, 2, 3, 4096);

        Assert.NotNull(region);
        Assert.Equal(0, region.Value.NormX, 6);
        Assert.Equal(0, region.Value.NormY, 6);
        Assert.Equal(0.5, region.Value.NormWidth, 6);
        Assert.Equal(0.5, region.Value.NormHeight, 6);
        // The viewport is 400x800 dp on a density-3 screen.
        Assert.Equal(1200, region.Value.PixelWidth);
        Assert.Equal(2400, region.Value.PixelHeight);
    }

    [Fact]
    public void Compute_ExcludesLetterboxBars()
    {
        // Square page centered in a 200x400 container: the 200x200 image sits
        // at y=100. Zoomed 2x on the bottom-right, the viewport covers local
        // (100,200)-(200,400), of which the image only has y 200-300.
        var image = DetailRegion.AspectFit(200, 400, 1.0);
        var visible = DetailRegion.VisibleLocalRect(200, 400, 2, -200, -400);

        var region = DetailRegion.Compute(visible, image, 2, 1, 4096);

        Assert.NotNull(region);
        Assert.Equal(new RectD(100, 200, 100, 100), region.Value.Local);
        Assert.Equal(0.5, region.Value.NormX, 6);
        Assert.Equal(0.5, region.Value.NormY, 6);
        Assert.Equal(0.5, region.Value.NormWidth, 6);
        Assert.Equal(0.5, region.Value.NormHeight, 6);
    }

    [Fact]
    public void Compute_ImageOutsideViewport_ReturnsNull()
    {
        var image = new RectD(0, 1000, 400, 800);
        var visible = new RectD(0, 0, 200, 400);

        Assert.Null(DetailRegion.Compute(visible, image, 2, 1, 4096));
    }

    [Fact]
    public void Compute_CapsLongerSideKeepingAspect()
    {
        var image = new RectD(0, 0, 1000, 2000);

        var region = DetailRegion.Compute(image, image, 4, 3, 2000);

        Assert.NotNull(region);
        Assert.Equal(2000, region.Value.PixelHeight);
        Assert.Equal(1000, region.Value.PixelWidth);
    }

    [Fact]
    public void Compute_InvalidInput_ReturnsNull()
    {
        var rect = new RectD(0, 0, 100, 100);

        Assert.Null(DetailRegion.Compute(rect, new RectD(0, 0, 0, 0), 2, 1, 4096));
        Assert.Null(DetailRegion.Compute(rect, rect, 0, 1, 4096));
        Assert.Null(DetailRegion.Compute(rect, rect, 2, 0, 4096));
    }
}
