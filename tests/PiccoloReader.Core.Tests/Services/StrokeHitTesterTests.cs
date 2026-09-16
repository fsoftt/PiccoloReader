using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class StrokeHitTesterTests
{
    [Fact]
    public void DistanceToPolyline_PointOnSegment_ReturnsZero()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(0.5, 0, points);

        Assert.Equal(0, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_PointAwayFromSegment_ReturnsPerpendicularDistance()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(0.5, 0.3, points);

        Assert.Equal(0.3, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_PointPastSegmentEnd_ReturnsDistanceToNearestEndpoint()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(2, 0, points);

        Assert.Equal(1, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_MultiSegmentPolyline_ReturnsMinimumAcrossSegments()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0), new(1, 1) };

        var distance = StrokeHitTester.DistanceToPolyline(1.1, 0.5, points);

        Assert.Equal(0.1, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_SinglePoint_ReturnsDistanceToThatPoint()
    {
        var points = new List<StrokePoint> { new(0, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(3, 4, points);

        Assert.Equal(5, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_EmptyList_ReturnsMaxValue()
    {
        var distance = StrokeHitTester.DistanceToPolyline(0, 0, new List<StrokePoint>());

        Assert.Equal(double.MaxValue, distance);
    }

    [Fact]
    public void DistanceToPolyline_ZeroLengthSegment_ReturnsDistanceToThatPoint()
    {
        var points = new List<StrokePoint> { new(2, 2), new(2, 2) };

        var distance = StrokeHitTester.DistanceToPolyline(2, 5, points);

        Assert.Equal(3, distance, precision: 10);
    }
}
