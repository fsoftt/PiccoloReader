using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class PageFrameTests
{
    [Fact]
    public void Fit_TallContainer_LetterboxesVertically()
    {
        // 100x200 container, square page -> 100x100 page centered vertically.
        var frame = PageFrame.Fit(100, 200, 1.0);

        Assert.Equal(0, frame.X, 6);
        Assert.Equal(0.25, frame.Y, 6);
        Assert.Equal(1, frame.Width, 6);
        Assert.Equal(0.5, frame.Height, 6);
    }

    [Fact]
    public void Fit_WideContainer_LetterboxesHorizontally()
    {
        // 200x100 container, square page -> 100x100 page centered horizontally.
        var frame = PageFrame.Fit(200, 100, 1.0);

        Assert.Equal(0.25, frame.X, 6);
        Assert.Equal(0, frame.Y, 6);
        Assert.Equal(0.5, frame.Width, 6);
        Assert.Equal(1, frame.Height, 6);
    }

    [Theory]
    [InlineData(0, 100, 1.0)]
    [InlineData(100, 0, 1.0)]
    [InlineData(100, 100, 0)]
    public void Fit_InvalidInput_ReturnsFull(double width, double height, double aspectRatio)
    {
        Assert.Equal(PageFrame.Full, PageFrame.Fit(width, height, aspectRatio));
    }

    [Fact]
    public void ToPageAndBack_RoundTrips()
    {
        var frame = PageFrame.Fit(1080, 1717, 1.414);

        Assert.Equal(0.3, frame.ToContainerX(frame.ToPageX(0.3)), 9);
        Assert.Equal(0.6, frame.ToContainerY(frame.ToPageY(0.6)), 9);
    }

    [Fact]
    public void ToPage_MapsPageEdgesToZeroAndOne()
    {
        var frame = PageFrame.Fit(100, 200, 1.0);

        Assert.Equal(0, frame.ToPageY(0.25), 9);
        Assert.Equal(1, frame.ToPageY(0.75), 9);
        Assert.Equal(0.5, frame.ToPageY(0.5), 9);
    }

    [Fact]
    public void Fit_WithFullCrop_EqualsUncroppedFit()
    {
        Assert.Equal(PageFrame.Fit(300, 500, 1.4), PageFrame.Fit(300, 500, 1.4, PageCrop.Full));
    }

    [Fact]
    public void Fit_WithCrop_VisibleRegionIsTheCroppedPartFitted()
    {
        var crop = new PageCrop(0.1, 0.2, 0.6, 0.7);
        var visible = PageFrame.Fit(300, 500, 1.4, crop).VisibleRegion(crop);
        var expected = PageFrame.Fit(300, 500, crop.CroppedAspectRatio(1.4));

        Assert.Equal(expected.X, visible.X, 9);
        Assert.Equal(expected.Y, visible.Y, 9);
        Assert.Equal(expected.Width, visible.Width, 9);
        Assert.Equal(expected.Height, visible.Height, 9);
    }

    [Fact]
    public void Fit_WithCrop_FullPageFrameExtendsPastTheContainer()
    {
        var crop = new PageCrop(0.25, 0.25, 0.75, 0.75);
        var frame = PageFrame.Fit(100, 100, 1.0, crop);

        Assert.Equal(2, frame.Width, 9);
        Assert.Equal(2, frame.Height, 9);
        Assert.Equal(-0.5, frame.X, 9);
        Assert.Equal(-0.5, frame.Y, 9);
    }

    [Fact]
    public void CropMapping_RoundTripsContainerAndPagePoints()
    {
        var crop = new PageCrop(0.1, 0.2, 0.6, 0.7);
        var frame = PageFrame.Fit(300, 500, 1.4, crop);
        var visible = frame.VisibleRegion(crop);

        Assert.Equal(visible.X, frame.ToContainerX(crop.Left), 9);
        Assert.Equal(visible.Y, frame.ToContainerY(crop.Top), 9);

        foreach (var (px, py) in new[] { (0.0, 0.0), (0.3, 0.4), (1.0, 1.0), (-0.1, 1.2) })
        {
            Assert.Equal(px, frame.ToPageX(frame.ToContainerX(px)), 9);
            Assert.Equal(py, frame.ToPageY(frame.ToContainerY(py)), 9);
        }
    }

    [Fact]
    public void CropMapping_PointOutsideCrop_LandsOutsideVisibleRegion()
    {
        var crop = new PageCrop(0.25, 0.25, 0.75, 0.75);
        var frame = PageFrame.Fit(100, 100, 1.0, crop);

        Assert.True(frame.ToContainerX(0.1) < 0);
        Assert.True(frame.ToContainerX(0.9) > 1);
        Assert.InRange(frame.ToContainerX(0.5), 0.0, 1.0);
    }

    [Fact]
    public void Fit_WithInvalidCrop_FallsBackToUncropped()
    {
        Assert.Equal(PageFrame.Fit(300, 500, 1.4), PageFrame.Fit(300, 500, 1.4, new PageCrop(0, 0, 0.05, 1)));
    }
}
