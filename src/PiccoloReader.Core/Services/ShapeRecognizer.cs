using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public enum RecognizedShapeKind
{
    None,
    Slur,
    Hairpin
}

// What a drawn stroke was recognized as. For a slur ControlPoints are the four
// Bezier points as page fractions; for a hairpin X/Y/Width/Height is the
// figure's box as page fractions. Unused members are default.
public sealed record RecognizedShape(
    RecognizedShapeKind Kind,
    bool Above = false,
    IReadOnlyList<(double X, double Y)>? ControlPoints = null,
    string? HairpinKey = null,
    double X = 0,
    double Y = 0,
    double Width = 0,
    double Height = 0)
{
    public static readonly RecognizedShape None = new(RecognizedShapeKind.None);
}

// Recognizes a hand-drawn slur or hairpin. It is deliberately conservative: a
// drawing that is not clearly one of them stays a plain stroke, because a
// stroke wrongly turned into a figure is worse than a figure left as a stroke.
//
// All geometry is done in "page-width units": x as is, y multiplied by the
// page's height / width, so angles and distances are the true ones on screen.
public static class ShapeRecognizer
{
    // Slur.
    private const double MinChord = 0.05;
    private const double MinPathLength = 0.06;
    private const double MinChordToPathRatio = 0.6;
    private const double MaxSlurAngleDegrees = 30;
    private const double MinBulgeToChord = 0.07;
    private const double MaxBulgeToChord = 0.5;
    private const double MaxOppositeSideOfBulge = 0.12;
    private const double MaxBacktrackOfChord = 0.08;
    private const double MinApexPosition = 0.25;
    private const double MaxApexPosition = 0.75;
    private const double MaxFitErrorOfBulge = 0.15;
    private const double FitErrorNoise = 0.002;

    // Hairpin.
    private const double MinArm = 0.03;
    private const double MinArmRatio = 0.6;
    private const double MinOpeningDegrees = 10;
    private const double MaxOpeningDegrees = 40;
    private const double MaxBisectorAngleDegrees = 20;
    private const double MaxArmBend = 0.06;

    // Two strokes making a hairpin: the shared end points may be this far
    // apart (page widths), and at most this fraction of the shorter stroke.
    private const double MaxJoinGap = 0.02;
    private const double MaxJoinGapOfArm = 0.35;

    // Points are page fractions; pageAspectRatio is the page's height / width.
    public static RecognizedShape Recognize(IReadOnlyList<StrokePoint> points, double pageAspectRatio)
    {
        if (points.Count < 6 || pageAspectRatio <= 0)
        {
            return RecognizedShape.None;
        }

        var path = ToUnits(points, pageAspectRatio);
        var hairpin = RecognizeHairpin(path, pageAspectRatio);
        return hairpin.Kind != RecognizedShapeKind.None ? hairpin : RecognizeSlur(path, pageAspectRatio);
    }

    // A hairpin made of two nearly straight strokes that share an end point.
    public static RecognizedShape RecognizeTwoStrokeHairpin(
        IReadOnlyList<StrokePoint> first, IReadOnlyList<StrokePoint> second, double pageAspectRatio)
    {
        if (first.Count < 2 || second.Count < 2 || pageAspectRatio <= 0)
        {
            return RecognizedShape.None;
        }

        var a = ToUnits(first, pageAspectRatio);
        var b = ToUnits(second, pageAspectRatio);
        if (!IsNearlyStraight(a) || !IsNearlyStraight(b))
        {
            return RecognizedShape.None;
        }

        var maxGap = Math.Min(MaxJoinGap, MaxJoinGapOfArm * Math.Min(Distance(a[0], a[^1]), Distance(b[0], b[^1])));

        // The closest pair of end points is the join; the far ends are the arms.
        var best = double.MaxValue;
        (double X, double Y) armA = default, armB = default, apex = default;
        foreach (var endA in new[] { 0, a.Count - 1 })
        {
            foreach (var endB in new[] { 0, b.Count - 1 })
            {
                var gap = Distance(a[endA], b[endB]);
                if (gap < best)
                {
                    best = gap;
                    armA = a[endA == 0 ? a.Count - 1 : 0];
                    armB = b[endB == 0 ? b.Count - 1 : 0];
                    apex = ((a[endA].X + b[endB].X) / 2, (a[endA].Y + b[endB].Y) / 2);
                }
            }
        }

        return best <= maxGap ? ClassifyHairpin(apex, armA, armB, pageAspectRatio) : RecognizedShape.None;
    }

    // True when a stroke is a single, nearly straight line (a candidate arm).
    public static bool IsNearlyStraight(IReadOnlyList<StrokePoint> points, double pageAspectRatio) =>
        points.Count >= 2 && pageAspectRatio > 0 && IsNearlyStraight(ToUnits(points, pageAspectRatio));

    private static bool IsNearlyStraight(IReadOnlyList<(double X, double Y)> path)
    {
        var chord = Distance(path[0], path[^1]);
        if (chord < MinArm)
        {
            return false;
        }

        var maxDeviation = 0.0;
        foreach (var p in path)
        {
            maxDeviation = Math.Max(maxDeviation, Math.Abs(SignedDistanceToLine(p, path[0], path[^1])));
        }

        return maxDeviation <= MaxArmBend * chord && PathLength(path) <= chord * 1.15;
    }

    // ---- Hairpin -----------------------------------------------------------

    private static RecognizedShape RecognizeHairpin(IReadOnlyList<(double X, double Y)> path, double aspect)
    {
        var length = PathLength(path);
        if (length < 2 * MinArm)
        {
            return RecognizedShape.None;
        }

        // Exactly one corner: start, apex, end.
        var simplified = Simplify(path, Math.Max(0.004, 0.03 * length));
        return simplified.Count == 3
            ? ClassifyHairpin(simplified[1], simplified[0], simplified[2], aspect)
            : RecognizedShape.None;
    }

    // apex is the corner; armA / armB are the free ends of the two arms.
    private static RecognizedShape ClassifyHairpin(
        (double X, double Y) apex, (double X, double Y) armA, (double X, double Y) armB, double aspect)
    {
        var lengthA = Distance(apex, armA);
        var lengthB = Distance(apex, armB);
        if (Math.Min(lengthA, lengthB) < MinArm || Math.Min(lengthA, lengthB) / Math.Max(lengthA, lengthB) < MinArmRatio)
        {
            return RecognizedShape.None;
        }

        var ax = (armA.X - apex.X) / lengthA;
        var ay = (armA.Y - apex.Y) / lengthA;
        var bx = (armB.X - apex.X) / lengthB;
        var by = (armB.Y - apex.Y) / lengthB;
        var opening = Math.Acos(Math.Clamp(ax * bx + ay * by, -1, 1)) * 180 / Math.PI;
        if (opening < MinOpeningDegrees || opening > MaxOpeningDegrees)
        {
            return RecognizedShape.None;
        }

        // The bisector points from the apex into the wedge: right for a
        // crescendo "<", left for a diminuendo ">".
        var sx = ax + bx;
        var sy = ay + by;
        var bisectorAngle = Math.Atan2(Math.Abs(sy), Math.Abs(sx)) * 180 / Math.PI;
        if (bisectorAngle > MaxBisectorAngleDegrees)
        {
            return RecognizedShape.None;
        }

        var crescendo = sx > 0;
        var armEndX = (armA.X + armB.X) / 2;
        var midY = (armA.Y + armB.Y) / 2;
        var width = crescendo ? armEndX - apex.X : apex.X - armEndX;
        var x = crescendo ? apex.X : armEndX;
        var height = Math.Abs(armA.Y - armB.Y) / aspect;
        if (width <= 0 || height <= 0)
        {
            return RecognizedShape.None;
        }

        var (clampedX, clampedWidth, clampedHeight) = HairpinGeometry.ClampBox(Math.Max(0, x), width, height, aspect);
        var y = Math.Clamp(midY / aspect - clampedHeight / 2, 0, Math.Max(0, 1 - clampedHeight));
        return new RecognizedShape(
            RecognizedShapeKind.Hairpin,
            HairpinKey: crescendo ? HairpinGeometry.CrescendoKey : HairpinGeometry.DecrescendoKey,
            X: clampedX, Y: y, Width: clampedWidth, Height: clampedHeight);
    }

    // ---- Slur --------------------------------------------------------------

    private static RecognizedShape RecognizeSlur(IReadOnlyList<(double X, double Y)> path, double aspect)
    {
        var length = PathLength(path);
        var start = path[0];
        var end = path[^1];
        var chord = Distance(start, end);
        if (length < MinPathLength || chord < MinChord || chord / length < MinChordToPathRatio)
        {
            return RecognizedShape.None;
        }

        // Roughly horizontal chord.
        var chordAngle = Math.Atan2(Math.Abs(end.Y - start.Y), Math.Abs(end.X - start.X)) * 180 / Math.PI;
        if (chordAngle > MaxSlurAngleDegrees)
        {
            return RecognizedShape.None;
        }

        // Measure along the chord, oriented left to right, and above / below
        // it. "Up" on screen is -y, so with u pointing right it is (uy, -ux).
        var origin = start.X <= end.X ? start : end;
        var ux = Math.Abs(end.X - start.X) / chord;
        var uy = (start.X <= end.X ? end.Y - start.Y : start.Y - end.Y) / chord;
        var nx = uy;
        var ny = -ux;
        var along = new double[path.Count];
        var side = new double[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var dx = path[i].X - origin.X;
            var dy = path[i].Y - origin.Y;
            along[i] = (dx * ux + dy * uy) / chord;
            side[i] = dx * nx + dy * ny;
        }

        // Which way the stroke bulges, and by how much.
        var positive = 0.0;
        var negative = 0.0;
        foreach (var s in side)
        {
            positive = Math.Max(positive, s);
            negative = Math.Max(negative, -s);
        }

        var above = positive >= negative;
        var bulge = above ? positive : negative;
        var opposite = above ? negative : positive;
        if (bulge / chord < MinBulgeToChord || bulge / chord > MaxBulgeToChord || opposite > MaxOppositeSideOfBulge * bulge)
        {
            return RecognizedShape.None;
        }

        // One bulge, in the middle part of the chord.
        var apexIndex = 0;
        for (var i = 1; i < side.Length; i++)
        {
            if (Math.Abs(side[i]) > Math.Abs(side[apexIndex]))
            {
                apexIndex = i;
            }
        }

        if (along[apexIndex] < MinApexPosition || along[apexIndex] > MaxApexPosition)
        {
            return RecognizedShape.None;
        }

        // Drawn in one direction, without doubling back or running far past
        // either end.
        var forward = along[^1] >= along[0];
        var runningMax = forward ? along[0] : -along[0];
        var worstBacktrack = 0.0;
        for (var i = 1; i < along.Length; i++)
        {
            if (along[i] < -0.12 || along[i] > 1.12)
            {
                return RecognizedShape.None;
            }

            var progress = forward ? along[i] : -along[i];
            runningMax = Math.Max(runningMax, progress);
            worstBacktrack = Math.Max(worstBacktrack, runningMax - progress);
        }

        if (worstBacktrack > MaxBacktrackOfChord)
        {
            return RecognizedShape.None;
        }

        // Not a corner (a "V" or an arrow head): a smooth arc needs more than
        // start, corner and end to stay within a fraction of its bulge.
        if (Simplify(path, 0.12 * bulge).Count <= 3)
        {
            return RecognizedShape.None;
        }

        if (SelfIntersects(Simplify(path, Math.Max(0.002, 0.01 * bulge))))
        {
            return RecognizedShape.None;
        }

        var control = FitBezier(path, length);
        if (control is null)
        {
            return RecognizedShape.None;
        }

        // The curve must stay close to what was drawn and bulge the same way.
        if (FitError(path, control) > MaxFitErrorOfBulge * bulge + FitErrorNoise)
        {
            return RecognizedShape.None;
        }

        var midpoint = SlurGeometry.Evaluate(control, 0.5);
        if ((midpoint.Y < (start.Y + end.Y) / 2) != above)
        {
            return RecognizedShape.None;
        }

        return new RecognizedShape(
            RecognizedShapeKind.Slur,
            Above: above,
            ControlPoints: control.Select(p => (p.X, p.Y / aspect)).ToArray());
    }

    // Least-squares cubic Bezier with the first and last point fixed; the
    // parameter of each sample is its fraction of the path length.
    private static (double X, double Y)[]? FitBezier(IReadOnlyList<(double X, double Y)> path, double length)
    {
        var p0 = path[0];
        var p3 = path[^1];
        double a11 = 0, a12 = 0, a22 = 0, bx1 = 0, bx2 = 0, by1 = 0, by2 = 0;
        var travelled = 0.0;
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                travelled += Distance(path[i - 1], path[i]);
            }

            var t = travelled / length;
            var u = 1 - t;
            var b0 = u * u * u;
            var b1 = 3 * u * u * t;
            var b2 = 3 * u * t * t;
            var b3 = t * t * t;
            var rx = path[i].X - b0 * p0.X - b3 * p3.X;
            var ry = path[i].Y - b0 * p0.Y - b3 * p3.Y;
            a11 += b1 * b1;
            a12 += b1 * b2;
            a22 += b2 * b2;
            bx1 += b1 * rx;
            bx2 += b2 * rx;
            by1 += b1 * ry;
            by2 += b2 * ry;
        }

        var det = a11 * a22 - a12 * a12;
        if (Math.Abs(det) < 1e-9)
        {
            return null;
        }

        var p1 = ((a22 * bx1 - a12 * bx2) / det, (a22 * by1 - a12 * by2) / det);
        var p2 = ((a11 * bx2 - a12 * bx1) / det, (a11 * by2 - a12 * by1) / det);
        return new[] { p0, p1, p2, p3 };
    }

    // The root-mean-square distance from the drawn points to the curve.
    private static double FitError(IReadOnlyList<(double X, double Y)> path, IReadOnlyList<(double X, double Y)> control)
    {
        const int samples = 64;
        var curve = new (double X, double Y)[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            curve[i] = SlurGeometry.Evaluate(control, (double)i / samples);
        }

        double sum = 0;
        foreach (var p in path)
        {
            var best = double.MaxValue;
            for (var i = 0; i < samples; i++)
            {
                best = Math.Min(best, DistanceToSegment(p, curve[i], curve[i + 1]));
            }

            sum += best * best;
        }

        return Math.Sqrt(sum / path.Count);
    }

    // ---- Geometry ----------------------------------------------------------

    private static List<(double X, double Y)> ToUnits(IReadOnlyList<StrokePoint> points, double aspect) =>
        points.Select(p => (p.X, p.Y * aspect)).ToList();

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double PathLength(IReadOnlyList<(double X, double Y)> path)
    {
        double length = 0;
        for (var i = 1; i < path.Count; i++)
        {
            length += Distance(path[i - 1], path[i]);
        }

        return length;
    }

    private static double SignedDistanceToLine((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length == 0 ? Distance(p, a) : ((p.X - a.X) * dy - (p.Y - a.Y) * dx) / length;
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return Distance(p, a);
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, (a.X + t * dx, a.Y + t * dy));
    }

    // Ramer-Douglas-Peucker; keeps the first and last point.
    private static List<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> path, double epsilon)
    {
        var keep = new bool[path.Count];
        keep[0] = true;
        keep[^1] = true;
        var stack = new Stack<(int From, int To)>();
        stack.Push((0, path.Count - 1));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            var worst = -1.0;
            var worstIndex = -1;
            for (var i = from + 1; i < to; i++)
            {
                var d = DistanceToSegment(path[i], path[from], path[to]);
                if (d > worst)
                {
                    worst = d;
                    worstIndex = i;
                }
            }

            if (worstIndex >= 0 && worst > epsilon)
            {
                keep[worstIndex] = true;
                stack.Push((from, worstIndex));
                stack.Push((worstIndex, to));
            }
        }

        var result = new List<(double X, double Y)>();
        for (var i = 0; i < path.Count; i++)
        {
            if (keep[i])
            {
                result.Add(path[i]);
            }
        }

        return result;
    }

    // Any two segments that are not neighbours crossing each other.
    private static bool SelfIntersects(IReadOnlyList<(double X, double Y)> path)
    {
        for (var i = 0; i < path.Count - 1; i++)
        {
            for (var j = i + 2; j < path.Count - 1; j++)
            {
                if (SegmentsCross(path[i], path[i + 1], path[j], path[j + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
}
