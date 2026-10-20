using System.Runtime.CompilerServices;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

// Finds the annotation an eraser touches. Strokes are parsed from JSON once
// (cached per annotation instance, with their bounding box) and skipped by a
// bounding-box test before the exact distance to the polyline is measured, so
// dragging the eraser over a page with many strokes stays cheap.
public static class AnnotationHitTester
{
    private sealed record StrokeInfo(IReadOnlyList<StrokePoint> Points, double MinX, double MaxX, double MinY, double MaxY);

    private static readonly ConditionalWeakTable<Annotation, StrokeInfo> Cache = new();

    /// <summary>
    /// The first annotation within radius of a page-normalized point. aspect is
    /// the full page height / width; the radius is in page-width units.
    /// </summary>
    public static Annotation? FindErasable(IEnumerable<Annotation> annotations, double x, double y, double aspect, double radius)
    {
        foreach (var a in annotations)
        {
            if (a.IsStroke)
            {
                var info = InfoOf(a);
                if (info.Points.Count == 0
                    || x < info.MinX - radius || x > info.MaxX + radius
                    || y * aspect < info.MinY * aspect - radius || y * aspect > info.MaxY * aspect + radius)
                {
                    continue;
                }

                if (StrokeHitTester.DistanceToPolyline(x, y, info.Points, aspect) <= radius)
                {
                    return a;
                }

                continue;
            }

            var radiusY = radius / aspect;
            if (x >= a.X - radius && x <= a.X + a.Width + radius &&
                y >= a.Y - radiusY && y <= a.Y + a.Height + radiusY)
            {
                return a;
            }
        }

        return null;
    }

    private static StrokeInfo InfoOf(Annotation a)
    {
        if (Cache.TryGetValue(a, out var cached))
        {
            return cached;
        }

        var points = AnnotationService.DeserializePoints(a.Points);
        var info = points.Count == 0
            ? new StrokeInfo(points, 0, 0, 0, 0)
            : new StrokeInfo(points, points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
        Cache.AddOrUpdate(a, info);
        return info;
    }
}
