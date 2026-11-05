using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

// Slurs are drawn as vector shapes instead of a music-font glyph: the shape is
// a cubic Bezier whose four control points are stored in Annotation.Points
// (normalized to the annotation's box, so resizing the box scales the curve).
// Without stored points a slur uses the default arc. Because the end points
// are free, a slur can also slope (one end higher than the other).
public static class SlurGeometry
{
    public const string AboveKey = "slurAbove";
    public const string BelowKey = "slurBelow";

    // The thickest part of the slur (its middle), as a multiple of the
    // hairpin stroke thickness; the tips taper to TipThicknessFactor of it.
    public const double MaxThicknessFactor = 1.9;
    public const double TipThicknessFactor = 0.25;

    // Never thinner than this in the middle, in rendered pixels, so a slur on
    // a small or zoomed-out page stays visible.
    public const double MinThicknessPx = 2.0;

    // Smallest box extent (page fractions) a curve is normalized against.
    public const double MinExtent = 1e-3;

    public const double MinWidth = 0.04;
    public const double MaxWidth = 1.0;

    // Bulge height limits, as a fraction of the page's width.
    public const double MinHeight = 0.008;
    public const double MaxHeight = 0.25;

    public static bool IsSlur(string? iconKey) => iconKey is AboveKey or BelowKey;

    public static bool IsAbove(string? iconKey) => iconKey != BelowKey;

    // Thickness at the middle of the slur, as a fraction of the page's width.
    public static double MaxThickness => HairpinGeometry.StrokeWidth * MaxThicknessFactor;

    // The default curve. The control points sit one third of the box beyond
    // the top (or bottom) edge, which puts the apex of the curve exactly on
    // that edge, so the box is the curve's tight bounds.
    public static IReadOnlyList<StrokePoint> DefaultPoints(bool above) => above
        ? new[] { new StrokePoint(0, 1), new StrokePoint(0.25, -1.0 / 3), new StrokePoint(0.75, -1.0 / 3), new StrokePoint(1, 1) }
        : new[] { new StrokePoint(0, 0), new StrokePoint(0.25, 4.0 / 3), new StrokePoint(0.75, 4.0 / 3), new StrokePoint(1, 0) };

    // The four control points (box fractions) of a slur annotation.
    public static IReadOnlyList<StrokePoint> ControlPoints(Annotation annotation)
    {
        // Points come from storage or a synced snapshot: anything unreadable or
        // not exactly four finite points falls back to the default arc.
        try
        {
            var stored = AnnotationService.DeserializePoints(annotation.Points);
            if (stored.Count == 4 && stored.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
            {
                return stored;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException)
        {
        }

        return DefaultPoints(IsAbove(annotation.IconKey));
    }

    // Evaluates the Bezier at t in [0, 1].
    public static (double X, double Y) Evaluate(IReadOnlyList<(double X, double Y)> p, double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        return (a * p[0].X + b * p[1].X + c * p[2].X + d * p[3].X,
                a * p[0].Y + b * p[1].Y + c * p[2].Y + d * p[3].Y);
    }

    // The tight bounds of the curve itself (not of its control points).
    public static (double MinX, double MinY, double MaxX, double MaxY) CurveBounds(IReadOnlyList<(double X, double Y)> p)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        const int steps = 64;
        for (var i = 0; i <= steps; i++)
        {
            var (x, y) = Evaluate(p, (double)i / steps);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return (minX, minY, maxX, maxY);
    }

    // Splits the shape into its two edges, in any consistent unit (pixels):
    // both run from the first to the last control point; the control points of
    // each are pushed apart perpendicular to the chord so the slur is
    // thickness wide in the middle and TipThicknessFactor of that at the tips
    // (so the ends never vanish). The end points move apart by half the tip
    // thickness; a Bezier's middle follows its end points by 1/4 and its
    // control points by 3/4, which fixes how far the control points go.
    public static ((double X, double Y)[] Outer, (double X, double Y)[] Inner) Edges(
        IReadOnlyList<(double X, double Y)> p, double thickness)
    {
        var dx = p[3].X - p[0].X;
        var dy = p[3].Y - p[0].Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        // The normal of the chord (zero chord: no thickness to place).
        var nx = length > 0 ? -dy / length : 0;
        var ny = length > 0 ? dx / length : 0;
        var tip = thickness / 2 * TipThicknessFactor;
        var shift = (thickness / 2 - 0.25 * tip) / 0.75;

        (double X, double Y)[] Side(double sign) =>
            new[]
            {
                (p[0].X + sign * nx * tip, p[0].Y + sign * ny * tip),
                (p[1].X + sign * nx * shift, p[1].Y + sign * ny * shift),
                (p[2].X + sign * nx * shift, p[2].Y + sign * ny * shift),
                (p[3].X + sign * nx * tip, p[3].Y + sign * ny * tip)
            };

        return (Side(1), Side(-1));
    }

    // Clamps a slur's box. Width and height are fractions of the page's own
    // width and height; pageAspectRatio is the page's height / width.
    public static (double Width, double Height) ClampSize(double width, double height, double pageAspectRatio)
    {
        var clampedWidth = Math.Clamp(width, MinWidth, MaxWidth);
        if (pageAspectRatio <= 0)
        {
            return (clampedWidth, height);
        }

        var bulge = Math.Clamp(height * pageAspectRatio, MinHeight, MaxHeight);
        return (clampedWidth, bulge / pageAspectRatio);
    }

    // The same, also keeping the slur inside the visible right edge.
    public static (double X, double Width, double Height) ClampBox(
        double x, double width, double height, double pageAspectRatio, double right = 1.0)
    {
        var (clampedWidth, clampedHeight) = ClampSize(width, height, pageAspectRatio);
        clampedWidth = Math.Min(clampedWidth, Math.Max(MinWidth, right - x));
        if (x + clampedWidth > right)
        {
            x = right - clampedWidth;
        }

        return (x, clampedWidth, clampedHeight);
    }

    // A slur drawn through page-normalized end points and control points
    // (page fractions): the annotation box is the curve's tight bounds and the
    // stored points are the control points relative to it.
    public static (double X, double Y, double Width, double Height, IReadOnlyList<StrokePoint> Points) FromPagePoints(
        IReadOnlyList<(double X, double Y)> controlPoints)
    {
        var (minX, minY, maxX, maxY) = CurveBounds(controlPoints);
        // A degenerate (flat or tiny) curve still gets a box with some extent; the
        // points are relative to that box, so the curve is reproduced exactly.
        var width = Math.Max(maxX - minX, MinExtent);
        var height = Math.Max(maxY - minY, MinExtent);
        var points = controlPoints
            .Select(c => new StrokePoint((c.X - minX) / width, (c.Y - minY) / height))
            .ToList();
        return (minX, minY, width, height, points);
    }
}
