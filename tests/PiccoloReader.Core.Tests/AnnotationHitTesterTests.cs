using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class AnnotationHitTesterTests
{
    private static Annotation Stroke(params (double X, double Y)[] points) => new()
    {
        Type = AnnotationType.Stroke,
        Points = AnnotationService.SerializePoints(points.Select(p => new StrokePoint(p.X, p.Y)).ToList())
    };

    [Fact]
    public void FindErasable_NearStroke_ReturnsIt()
    {
        var stroke = Stroke((0.1, 0.1), (0.5, 0.1));

        Assert.Same(stroke, AnnotationHitTester.FindErasable(new[] { stroke }, 0.3, 0.105, 1.4, 0.02));
    }

    [Fact]
    public void FindErasable_FarFromStroke_ReturnsNull()
    {
        var stroke = Stroke((0.1, 0.1), (0.5, 0.1));

        Assert.Null(AnnotationHitTester.FindErasable(new[] { stroke }, 0.3, 0.5, 1.4, 0.02));
        Assert.Null(AnnotationHitTester.FindErasable(new[] { stroke }, 0.9, 0.1, 1.4, 0.02));
    }

    [Fact]
    public void FindErasable_VerticalDistanceUsesPageAspect()
    {
        var stroke = Stroke((0.1, 0.1), (0.5, 0.1));

        // 0.01 down is 0.014 page widths on a 1.4 aspect page: inside 0.02.
        Assert.Same(stroke, AnnotationHitTester.FindErasable(new[] { stroke }, 0.3, 0.11, 1.4, 0.02));
        // 0.02 down is 0.028 page widths: outside.
        Assert.Null(AnnotationHitTester.FindErasable(new[] { stroke }, 0.3, 0.12, 1.4, 0.02));
    }

    [Fact]
    public void FindErasable_IconUsesItsBoxPlusRadius()
    {
        var icon = new Annotation { X = 0.4, Y = 0.4, Width = 0.1, Height = 0.05 };

        Assert.Same(icon, AnnotationHitTester.FindErasable(new[] { icon }, 0.45, 0.42, 1.4, 0.02));
        Assert.Null(AnnotationHitTester.FindErasable(new[] { icon }, 0.8, 0.8, 1.4, 0.02));
    }
}
