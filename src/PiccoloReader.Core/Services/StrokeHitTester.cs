using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public static class StrokeHitTester
{
    public static double DistanceToPolyline(double pointX, double pointY, IReadOnlyList<StrokePoint> points)
    {
        if (points.Count == 0)
        {
            return double.MaxValue;
        }

        if (points.Count == 1)
        {
            return Distance(pointX, pointY, points[0].X, points[0].Y);
        }

        var minDistance = double.MaxValue;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var distance = DistanceToSegment(pointX, pointY, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y);
            minDistance = Math.Min(minDistance, distance);
        }

        return minDistance;
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = abx * abx + aby * aby;

        if (lengthSquared == 0)
        {
            return Distance(px, py, ax, ay);
        }

        var t = ((px - ax) * abx + (py - ay) * aby) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var closestX = ax + t * abx;
        var closestY = ay + t * aby;

        return Distance(px, py, closestX, closestY);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
