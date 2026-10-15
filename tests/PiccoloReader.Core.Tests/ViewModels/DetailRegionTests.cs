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
    public void Compute_SnapsRegionSizeToWholePixels()
    {
        // 411.43 units at density 2.625 is 1080 px; at 3x zoom the visible
        // part is 137.14 units = 360 px (+ float noise): it must come out as
        // exactly 360 px, not 361, so the overlay is not magnified.
        var image = new RectD(0, 0, 1080 / 2.625, 582);
        var visible = DetailRegion.VisibleLocalRect(1080 / 2.625, 841.5, 3, -411.12, -435.4);

        var region = DetailRegion.Compute(visible, image, 3, 2.625, 4096)!.Value;

        Assert.Equal(360, region.Local.Width * 2.625, 9);
        Assert.Equal(Math.Round(region.Local.Height * 2.625), region.Local.Height * 2.625, 9);
        Assert.Equal(region.Local.Width / image.Width, region.NormWidth, 9);
    }

    [Fact]
    public void SnapToPixels_MovesEveryEdgeToAWholePixel()
    {
        var snapped = DetailRegion.SnapToPixels(new RectD(137.04047, 221.32597, 137.1429, 280.5082), 2.625);

        Assert.Equal(Math.Round(snapped.X * 2.625), snapped.X * 2.625, 9);
        Assert.Equal(Math.Round(snapped.Y * 2.625), snapped.Y * 2.625, 9);
        Assert.Equal(Math.Round(snapped.Right * 2.625), snapped.Right * 2.625, 9);
        Assert.Equal(Math.Round(snapped.Bottom * 2.625), snapped.Bottom * 2.625, 9);
        // Nearest pixel, so no edge moved by more than half of one.
        Assert.True(Math.Abs(snapped.X - 137.04047) * 2.625 <= 0.5 + 1e-9);
        Assert.True(Math.Abs(snapped.Bottom - (221.32597 + 280.5082)) * 2.625 <= 0.5 + 1e-9);
    }

    [Fact]
    public void SnapToPixels_NeverCollapsesBelowOnePixel()
    {
        var snapped = DetailRegion.SnapToPixels(new RectD(10.01, 10.01, 0.01, 0.01), 2);

        Assert.True(snapped.Width >= 0.5 - 1e-9);
        Assert.True(snapped.Height >= 0.5 - 1e-9);
    }

    [Theory]
    [InlineData(137.142857142857, 2.625, 3)]
    [InlineData(280.508, 2.625, 3)]
    [InlineData(100.3, 3.0, 3)]
    public void LayoutRequest_RoundsAndCeilsToTheSamePixelCount(double units, double density, double _)
    {
        var px = Math.Round(units * density);
        var request = DetailRegion.LayoutRequest(units, density);

        Assert.Equal(px, Math.Ceiling(request * density));
        Assert.Equal(px, Math.Round(request * density));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.3, 0.7)]
    [InlineData(0.62, 0.41)]
    public void DetailAndBase_PlaceAPagePointAtTheSameSpot(double pageX, double pageY)
    {
        // Base: the page fills the image rect. Detail: a bitmap of the region
        // stretched over Local. A page point must land at the same local
        // position through both, i.e. within 0.5 device px at any zoom.
        const double density = 2.625;
        var image = DetailRegion.AspectFit(1080 / density, 841.5, 1528.0 / 1080.0);
        foreach (var zoom in new[] { 2.0, 3.0, 5.0 })
        {
            var visible = DetailRegion.VisibleLocalRect(1080 / density, 841.5, zoom, -zoom * 53.7, -zoom * 120.3);
            var region = DetailRegion.Compute(visible, image, zoom, density, 4096)!.Value;

            var baseX = image.X + pageX * image.Width;
            var baseY = image.Y + pageY * image.Height;
            if (baseX < region.Local.X || baseX > region.Local.Right || baseY < region.Local.Y || baseY > region.Local.Bottom)
            {
                continue;
            }

            // Position of the point inside the region bitmap, then drawn over Local.
            var u = (pageX - region.NormX) / region.NormWidth;
            var v = (pageY - region.NormY) / region.NormHeight;
            var detailX = region.Local.X + u * region.Local.Width;
            var detailY = region.Local.Y + v * region.Local.Height;

            Assert.True(Math.Abs(detailX - baseX) * density * zoom <= 0.5, $"x at {zoom}x");
            Assert.True(Math.Abs(detailY - baseY) * density * zoom <= 0.5, $"y at {zoom}x");
        }
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
