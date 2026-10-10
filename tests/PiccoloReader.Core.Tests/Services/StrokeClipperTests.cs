using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Services;

public class StrokeClipperTests
{
    [Fact]
    public void Inside_stroke_is_unchanged()
    {
        var pts = new[] { new StrokePoint(0.1, 0.1), new StrokePoint(0.5, 0.5) };
        var result = StrokeClipper.Clip(pts, PageCrop.Full);
        Assert.Single(result);
        Assert.Equal(pts, result[0]);
    }

    [Fact]
    public void Stroke_fully_outside_page_is_dropped()
    {
        var pts = new[] { new StrokePoint(1.2, 0.1), new StrokePoint(1.5, 0.5) };
        Assert.Empty(StrokeClipper.Clip(pts, PageCrop.Full));
    }

    [Fact]
    public void Stroke_fully_outside_crop_is_dropped()
    {
        var pts = new[] { new StrokePoint(0.1, 0.1), new StrokePoint(0.2, 0.2) };
        Assert.Empty(StrokeClipper.Clip(pts, new PageCrop(0.5, 0.5, 1, 1)));
    }

    [Fact]
    public void Stroke_crossing_the_edge_is_cut_at_the_edge()
    {
        var pts = new[] { new StrokePoint(0.5, 0.5), new StrokePoint(1.5, 0.5) };
        var result = StrokeClipper.Clip(pts, PageCrop.Full);
        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(1.0, result[0][1].X, 6);
    }

    [Fact]
    public void Stroke_leaving_and_reentering_is_split()
    {
        var pts = new[] { new StrokePoint(0.2, 0.5), new StrokePoint(1.4, 0.5), new StrokePoint(1.4, 0.6), new StrokePoint(0.3, 0.6) };
        var result = StrokeClipper.Clip(pts, PageCrop.Full);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Single_point_left_after_clip_is_dropped()
    {
        var pts = new[] { new StrokePoint(0.99, 0.5), new StrokePoint(1.0, 0.5), new StrokePoint(1.3, 0.5) };
        var result = StrokeClipper.Clip(pts, PageCrop.Full);
        Assert.Single(result);
        Assert.All(result[0], p => Assert.InRange(p.X, 0, 1));
    }
}
