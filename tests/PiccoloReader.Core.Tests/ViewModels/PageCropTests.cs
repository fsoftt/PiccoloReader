using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class PageCropTests
{
    private static readonly PageCrop Sample = new(0.1, 0.2, 0.7, 0.8);

    [Fact]
    public void Full_IsFullAndValid()
    {
        Assert.True(PageCrop.Full.IsFull);
        Assert.True(PageCrop.Full.IsValid);
        Assert.False(Sample.IsFull);
    }

    [Theory]
    [InlineData(-0.1, 0, 1, 1)]
    [InlineData(0, 0, 1.2, 1)]
    [InlineData(0, 0, 0.1, 1)]
    [InlineData(0, 0.5, 1, 0.6)]
    public void IsValid_RejectsOutOfPageOrTooSmall(double l, double t, double r, double b)
    {
        Assert.False(new PageCrop(l, t, r, b).IsValid);
        Assert.True(new PageCrop(l, t, r, b).OrFull().IsFull);
    }

    [Fact]
    public void PageAndCroppedCoordinates_RoundTrip()
    {
        var cropped = Sample.ToCroppedX(0.4);
        Assert.Equal(0.5, cropped, 9);
        Assert.Equal(0.4, Sample.ToPageX(cropped), 9);
        Assert.Equal(0.5, Sample.ToCroppedY(0.5), 9);
        Assert.Equal(0.5, Sample.ToPageY(0.5), 9);
    }

    [Fact]
    public void PointOutsideCrop_MapsOutsideZeroToOne()
    {
        Assert.True(Sample.ToCroppedX(0.05) < 0);
        Assert.True(Sample.ToCroppedX(0.9) > 1);
        Assert.False(Sample.Contains(0.05, 0.5));
        Assert.True(Sample.Contains(0.4, 0.5));
    }

    [Fact]
    public void FullCrop_IsIdentity()
    {
        Assert.Equal(0.37, PageCrop.Full.ToCroppedX(0.37), 12);
        Assert.Equal(0.37, PageCrop.Full.ToPageY(0.37), 12);
        Assert.Equal(1.4, PageCrop.Full.CroppedAspectRatio(1.4), 12);
    }

    [Fact]
    public void AspectRatios_RoundTrip()
    {
        var wide = new PageCrop(0, 0.25, 0.5, 0.75);
        Assert.Equal(1.4, wide.FullAspectRatio(wide.CroppedAspectRatio(1.4)), 9);
        Assert.Equal(1.4, wide.CroppedAspectRatio(1.4), 9);
        Assert.Equal(2.8, new PageCrop(0, 0, 0.5, 1).CroppedAspectRatio(1.4), 9);
    }

    [Fact]
    public void Drag_Edge_MovesOnlyThatEdge()
    {
        var c = Sample.Drag(CropHandle.Left, 0.05, 0.3);
        Assert.Equal(0.15, c.Left, 9);
        Assert.Equal(Sample.Top, c.Top, 9);
        Assert.Equal(Sample.Right, c.Right, 9);
        Assert.Equal(Sample.Bottom, c.Bottom, 9);
    }

    [Fact]
    public void Drag_Corner_MovesBothEdges()
    {
        var c = Sample.Drag(CropHandle.BottomRight, 0.1, 0.1);
        Assert.Equal(0.8, c.Right, 9);
        Assert.Equal(0.9, c.Bottom, 9);
        Assert.Equal(0.1, c.Left, 9);
    }

    [Fact]
    public void Drag_ClampsToPageAndMinimumSize()
    {
        var pastEdge = Sample.Drag(CropHandle.TopLeft, -1, -1);
        Assert.Equal(0, pastEdge.Left, 9);
        Assert.Equal(0, pastEdge.Top, 9);

        var squeezed = Sample.Drag(CropHandle.Right, -5, 0);
        Assert.Equal(Sample.Left + PageCrop.MinSize, squeezed.Right, 9);
        Assert.Equal(Sample.Left, squeezed.Left, 9);

        var bottom = Sample.Drag(CropHandle.Top, 5, 5);
        Assert.Equal(Sample.Bottom - PageCrop.MinSize, bottom.Top, 9);
        Assert.True(bottom.IsValid);
    }
}
