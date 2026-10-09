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
}
