using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class SlurGeometryTests
{
    private static (double X, double Y)[] Pairs(IEnumerable<StrokePoint> points) =>
        points.Select(p => (p.X, p.Y)).ToArray();

    [Fact]
    public void Catalog_HasBothSlurs()
    {
        Assert.NotNull(MusicIconCatalog.FindByKey(SlurGeometry.AboveKey));
        Assert.NotNull(MusicIconCatalog.FindByKey(SlurGeometry.BelowKey));
    }

    [Fact]
    public void IsSlur_OnlyForSlurKeys()
    {
        Assert.True(SlurGeometry.IsSlur("slurAbove"));
        Assert.True(SlurGeometry.IsSlur("slurBelow"));
        Assert.False(SlurGeometry.IsSlur("dynamicForte"));
        Assert.False(SlurGeometry.IsSlur(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultCurve_FillsItsBoxTightly(bool above)
    {
        var (minX, minY, maxX, maxY) = SlurGeometry.CurveBounds(Pairs(SlurGeometry.DefaultPoints(above)));

        Assert.Equal(0, minX, 3);
        Assert.Equal(1, maxX, 3);
        Assert.Equal(0, minY, 3);
        Assert.Equal(1, maxY, 3);
    }

    [Fact]
    public void DefaultCurve_AboveBulgesUp_BelowBulgesDown()
    {
        var above = SlurGeometry.Evaluate(Pairs(SlurGeometry.DefaultPoints(true)), 0.5);
        var below = SlurGeometry.Evaluate(Pairs(SlurGeometry.DefaultPoints(false)), 0.5);

        Assert.True(above.Y < 0.1);
        Assert.True(below.Y > 0.9);
    }

    [Fact]
    public void ControlPoints_NoStoredPoints_UsesDefault()
    {
        var slur = new Annotation { IconKey = SlurGeometry.BelowKey };

        Assert.Equal(SlurGeometry.DefaultPoints(false), SlurGeometry.ControlPoints(slur));
    }

    [Fact]
    public void ControlPoints_StoredPoints_AreUsed()
    {
        var stored = new[] { new StrokePoint(0, 0.2), new StrokePoint(0.3, 0), new StrokePoint(0.7, 0), new StrokePoint(1, 1) };
        var slur = new Annotation { IconKey = SlurGeometry.AboveKey, Points = AnnotationService.SerializePoints(stored) };

        Assert.Equal(stored, SlurGeometry.ControlPoints(slur));
    }

    [Fact]
    public void ControlPoints_WrongCount_FallsBackToDefault()
    {
        var slur = new Annotation
        {
            IconKey = SlurGeometry.AboveKey,
            Points = AnnotationService.SerializePoints(new[] { new StrokePoint(0, 0) })
        };

        Assert.Equal(4, SlurGeometry.ControlPoints(slur).Count);
    }

    [Fact]
    public void Edges_TaperFromThicknessInTheMiddleToTipFactorAtTheEnds()
    {
        var p = new (double X, double Y)[] { (0, 100), (25, 0), (75, 0), (100, 100) };

        var (outer, inner) = SlurGeometry.Edges(p, 8);

        static double Gap((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        Assert.Equal(8, Gap(SlurGeometry.Evaluate(outer, 0.5), SlurGeometry.Evaluate(inner, 0.5)), 6);
        Assert.Equal(8 * SlurGeometry.TipThicknessFactor, Gap(outer[0], inner[0]), 6);
        Assert.Equal(8 * SlurGeometry.TipThicknessFactor, Gap(outer[3], inner[3]), 6);
        // The tips stay centered on the end points.
        Assert.Equal(p[0].X, (outer[0].X + inner[0].X) / 2, 6);
        Assert.Equal(p[3].Y, (outer[3].Y + inner[3].Y) / 2, 6);
    }

    [Fact]
    public void Edges_ZeroLengthChord_DoesNotThrow()
    {
        var p = new (double X, double Y)[] { (5, 5), (5, 0), (5, 0), (5, 5) };

        var (outer, inner) = SlurGeometry.Edges(p, 4);

        Assert.Equal(p[1], outer[1]);
        Assert.Equal(p[1], inner[1]);
    }

    [Fact]
    public void ClampSize_LimitsWidthAndBulge()
    {
        var (width, height) = SlurGeometry.ClampSize(5, 5, 1.4);
        Assert.Equal(SlurGeometry.MaxWidth, width);
        Assert.Equal(SlurGeometry.MaxHeight / 1.4, height, 6);

        (width, height) = SlurGeometry.ClampSize(0.001, 0.0001, 1.4);
        Assert.Equal(SlurGeometry.MinWidth, width);
        Assert.Equal(SlurGeometry.MinHeight / 1.4, height, 6);
    }

    [Fact]
    public void ClampBox_KeepsInsideRightEdge()
    {
        var (x, width, _) = SlurGeometry.ClampBox(0.9, 0.3, 0.05, 1.4, 1.0);

        Assert.True(x + width <= 1.0 + 1e-9);
    }

    [Fact]
    public void FromPagePoints_BoxIsCurveBounds_AndPointsAreBoxRelative()
    {
        var page = new (double X, double Y)[] { (0.2, 0.5), (0.3, 0.3), (0.5, 0.3), (0.6, 0.5) };

        var (x, y, width, height, points) = SlurGeometry.FromPagePoints(page);

        Assert.Equal(0.2, x, 6);
        Assert.Equal(0.6, x + width, 6);
        Assert.Equal(0.5, y + height, 6);
        // Mapping the stored points back through the box restores the page curve.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(page[i].X, x + points[i].X * width, 6);
            Assert.Equal(page[i].Y, y + points[i].Y * height, 6);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2,3]")]
    [InlineData("[]")]
    public void ControlPoints_MalformedPoints_FallBackToDefault(string json)
    {
        var slur = new Annotation { IconKey = SlurGeometry.AboveKey, Points = json };

        Assert.Equal(SlurGeometry.DefaultPoints(true), SlurGeometry.ControlPoints(slur));
    }

    [Fact]
    public void FromPagePoints_DegenerateCurve_StillGivesABoxWithExtent()
    {
        var flat = new (double X, double Y)[] { (0.2, 0.5), (0.3, 0.5), (0.4, 0.5), (0.5, 0.5) };

        var (_, _, width, height, points) = SlurGeometry.FromPagePoints(flat);

        Assert.True(width > 0 && height >= SlurGeometry.MinExtent);
        Assert.All(points, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }
}
