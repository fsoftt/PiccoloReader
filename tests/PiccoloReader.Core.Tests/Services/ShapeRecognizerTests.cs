using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class ShapeRecognizerTests
{
    private const double Aspect = 1.4;

    // A deterministic wobble so strokes look hand-drawn.
    private static double Jitter(int i, double amount) => Math.Sin(i * 12.9898) * amount;

    private static List<StrokePoint> Polyline(params (double X, double Y)[] corners)
    {
        var points = new List<StrokePoint>();
        var n = 0;
        for (var c = 0; c < corners.Length - 1; c++)
        {
            for (var i = 0; i < 15; i++)
            {
                var t = i / 15.0;
                points.Add(new StrokePoint(
                    corners[c].X + (corners[c + 1].X - corners[c].X) * t + Jitter(n, 0.0008),
                    corners[c].Y + (corners[c + 1].Y - corners[c].Y) * t + Jitter(n + 7, 0.0008)));
                n++;
            }
        }

        points.Add(new StrokePoint(corners[^1].X, corners[^1].Y));
        return points;
    }

    // An arc from (x0, y) to (x1, y + y2Offset) bulging by bulge (page widths), up when positive.
    private static List<StrokePoint> Arc(double x0, double x1, double y, double bulge, double y2Offset = 0)
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;
            var x = x0 + (x1 - x0) * t;
            var baseY = y + y2Offset * t;
            var yy = baseY - 4 * bulge * t * (1 - t) / Aspect;
            points.Add(new StrokePoint(x + Jitter(i, 0.0005), yy + Jitter(i + 3, 0.0005)));
        }

        return points;
    }

    private static RecognizedShape Recognize(IReadOnlyList<StrokePoint> points) => ShapeRecognizer.Recognize(points, Aspect);

    [Fact]
    public void Slur_ArcBulgingUp_IsSlurAbove()
    {
        var shape = Recognize(Arc(0.2, 0.4, 0.5, 0.04));

        Assert.Equal(RecognizedShapeKind.Slur, shape.Kind);
        Assert.True(shape.Above);
        Assert.Equal(4, shape.ControlPoints!.Count);
        Assert.Equal(0.2, shape.ControlPoints[0].X, 2);
        Assert.Equal(0.4, shape.ControlPoints[3].X, 2);
    }

    [Fact]
    public void Slur_ArcBulgingDown_IsSlurBelow()
    {
        var shape = Recognize(Arc(0.2, 0.4, 0.5, -0.04));

        Assert.Equal(RecognizedShapeKind.Slur, shape.Kind);
        Assert.False(shape.Above);
    }

    [Fact]
    public void Slur_RightToLeftStroke_IsStillSlur()
    {
        var shape = Recognize(Arc(0.2, 0.4, 0.5, 0.04).AsEnumerable().Reverse().ToList());

        Assert.Equal(RecognizedShapeKind.Slur, shape.Kind);
        Assert.True(shape.Above);
    }

    [Fact]
    public void Slur_Tilted_IsRecognizedWithDifferentEndHeights()
    {
        var shape = Recognize(Arc(0.2, 0.4, 0.5, 0.04, y2Offset: -0.03));

        Assert.Equal(RecognizedShapeKind.Slur, shape.Kind);
        Assert.NotEqual(shape.ControlPoints![0].Y, shape.ControlPoints[3].Y, 3);
    }

    [Fact]
    public void Slur_PageFractionsRoundTripThroughFigureBox()
    {
        var shape = Recognize(Arc(0.2, 0.4, 0.5, 0.04));

        var (x, y, w, h, _) = SlurGeometry.FromPagePoints(shape.ControlPoints!);

        Assert.InRange(x, 0.19, 0.21);
        Assert.InRange(w, 0.19, 0.21);
        Assert.True(h > 0 && y > 0.3 && y + h < 0.6);
    }

    [Fact]
    public void Straight_Line_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Polyline((0.2, 0.5), (0.5, 0.5))).Kind);
    }

    [Fact]
    public void ShallowWobble_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Arc(0.2, 0.5, 0.5, 0.004)).Kind);
    }

    [Fact]
    public void Scribble_IsNothing()
    {
        var scribble = Polyline((0.2, 0.5), (0.4, 0.46), (0.25, 0.44), (0.42, 0.52), (0.22, 0.54), (0.4, 0.5));

        Assert.Equal(RecognizedShapeKind.None, Recognize(scribble).Kind);
    }

    [Fact]
    public void SCurve_IsNothing()
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;
            points.Add(new StrokePoint(0.2 + 0.2 * t, 0.5 - 0.02 * Math.Sin(t * 2 * Math.PI)));
        }

        Assert.Equal(RecognizedShapeKind.None, Recognize(points).Kind);
    }

    [Fact]
    public void Circle_IsNothing()
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 50; i++)
        {
            var a = i / 50.0 * 2 * Math.PI;
            points.Add(new StrokePoint(0.4 + 0.05 * Math.Cos(a), 0.5 + 0.05 * Math.Sin(a) / Aspect));
        }

        Assert.Equal(RecognizedShapeKind.None, Recognize(points).Kind);
    }

    [Fact]
    public void VShape_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Polyline((0.2, 0.45), (0.3, 0.52), (0.4, 0.45))).Kind);
    }

    [Fact]
    public void Arrow_IsNothing()
    {
        var arrow = Polyline((0.2, 0.5), (0.5, 0.5), (0.45, 0.48), (0.5, 0.5), (0.45, 0.52));

        Assert.Equal(RecognizedShapeKind.None, Recognize(arrow).Kind);
    }

    [Fact]
    public void Zigzag_IsNothing()
    {
        var zigzag = Polyline((0.2, 0.5), (0.25, 0.46), (0.3, 0.5), (0.35, 0.46), (0.4, 0.5));

        Assert.Equal(RecognizedShapeKind.None, Recognize(zigzag).Kind);
    }

    [Fact]
    public void ShortStroke_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Arc(0.2, 0.23, 0.5, 0.006)).Kind);
    }

    [Fact]
    public void SteepArc_IsNothing()
    {
        // Chord more than 30 degrees from horizontal.
        Assert.Equal(RecognizedShapeKind.None, Recognize(Arc(0.2, 0.4, 0.5, 0.04, y2Offset: -0.2)).Kind);
    }

    [Fact]
    public void Hairpin_ApexLeft_IsCrescendo()
    {
        var shape = Recognize(Polyline((0.3, 0.5 - 0.02), (0.1, 0.5), (0.3, 0.5 + 0.02)));

        Assert.Equal(RecognizedShapeKind.Hairpin, shape.Kind);
        Assert.Equal(HairpinGeometry.CrescendoKey, shape.HairpinKey);
        Assert.InRange(shape.X, 0.09, 0.11);
        Assert.InRange(shape.Width, 0.19, 0.21);
    }

    [Fact]
    public void Hairpin_ApexRight_IsDecrescendo()
    {
        var shape = Recognize(Polyline((0.1, 0.5 - 0.02), (0.3, 0.5), (0.1, 0.5 + 0.02)));

        Assert.Equal(RecognizedShapeKind.Hairpin, shape.Kind);
        Assert.Equal(HairpinGeometry.DecrescendoKey, shape.HairpinKey);
        Assert.InRange(shape.X, 0.09, 0.11);
    }

    [Fact]
    public void Hairpin_TooWideOpening_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Polyline((0.3, 0.4), (0.1, 0.5), (0.3, 0.6))).Kind);
    }

    [Fact]
    public void Hairpin_UnequalArms_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Polyline((0.15, 0.49), (0.1, 0.5), (0.4, 0.52))).Kind);
    }

    [Fact]
    public void Hairpin_Tilted_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(Polyline((0.3, 0.3), (0.1, 0.2), (0.3, 0.22))).Kind);
    }

    [Fact]
    public void TwoStrokeHairpin_SharedApex_IsRecognized()
    {
        var upper = Polyline((0.1, 0.5), (0.3, 0.5 - 0.02));
        var lower = Polyline((0.102, 0.501), (0.3, 0.5 + 0.02));

        var shape = ShapeRecognizer.RecognizeTwoStrokeHairpin(upper, lower, Aspect);

        Assert.Equal(RecognizedShapeKind.Hairpin, shape.Kind);
        Assert.Equal(HairpinGeometry.CrescendoKey, shape.HairpinKey);
    }

    [Fact]
    public void TwoStrokeHairpin_FarApart_IsNothing()
    {
        var upper = Polyline((0.1, 0.5), (0.3, 0.5 - 0.02));
        var lower = Polyline((0.15, 0.55), (0.35, 0.55 + 0.02));

        Assert.Equal(RecognizedShapeKind.None, ShapeRecognizer.RecognizeTwoStrokeHairpin(upper, lower, Aspect).Kind);
    }

    [Fact]
    public void TwoStrokeHairpin_CurvedStroke_IsNothing()
    {
        var curved = Arc(0.1, 0.3, 0.5, 0.04);
        var lower = Polyline((0.1, 0.5), (0.3, 0.52));

        Assert.Equal(RecognizedShapeKind.None, ShapeRecognizer.RecognizeTwoStrokeHairpin(curved, lower, Aspect).Kind);
    }

    [Fact]
    public void IsNearlyStraight_DistinguishesLineFromArc()
    {
        Assert.True(ShapeRecognizer.IsNearlyStraight(Polyline((0.1, 0.5), (0.3, 0.48)), Aspect));
        Assert.False(ShapeRecognizer.IsNearlyStraight(Arc(0.1, 0.3, 0.5, 0.04), Aspect));
    }

    [Fact]
    public void TooFewPoints_IsNothing()
    {
        Assert.Equal(RecognizedShapeKind.None, Recognize(new[] { new StrokePoint(0, 0), new StrokePoint(1, 1) }).Kind);
    }
}
