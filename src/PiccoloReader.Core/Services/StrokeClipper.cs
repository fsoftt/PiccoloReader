using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

// Keeps the part of a stroke that lies on the page and inside the visible
// crop. Points are normalized (0-1) to the full page; a stroke that leaves
// and re-enters the area comes back as several pieces.
public static class StrokeClipper
{
    public static IReadOnlyList<IReadOnlyList<StrokePoint>> Clip(IReadOnlyList<StrokePoint> points, PageCrop crop)
    {
        var area = crop.IsValid ? crop : PageCrop.Full;
        var pieces = new List<IReadOnlyList<StrokePoint>>();
        var current = new List<StrokePoint>();

        void Flush()
        {
            if (current.Count >= 2)
            {
                pieces.Add(current);
            }

            current = new List<StrokePoint>();
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (i == 0)
            {
                if (Inside(points[0], area))
                {
                    current.Add(points[0]);
                }

                continue;
            }

            var a = points[i - 1];
            var b = points[i];
            if (!TryClipSegment(a, b, area, out var t0, out var t1))
            {
                Flush();
                continue;
            }

            var start = Lerp(a, b, t0);
            var end = Lerp(a, b, t1);
            if (t0 > 0)
            {
                Flush();
                current.Add(start);
            }
            else if (current.Count == 0)
            {
                current.Add(start);
            }

            current.Add(end);
            if (t1 < 1)
            {
                Flush();
            }
        }

        Flush();
        return pieces;
    }

    private static bool Inside(StrokePoint p, PageCrop r) => r.Contains(p.X, p.Y);

    private static StrokePoint Lerp(StrokePoint a, StrokePoint b, double t) =>
        t <= 0 ? a : t >= 1 ? b : new StrokePoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    // Liang-Barsky.
    private static bool TryClipSegment(StrokePoint a, StrokePoint b, PageCrop r, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Test(-dx, a.X - r.Left, ref t0, ref t1) &&
               Test(dx, r.Right - a.X, ref t0, ref t1) &&
               Test(-dy, a.Y - r.Top, ref t0, ref t1) &&
               Test(dy, r.Bottom - a.Y, ref t0, ref t1);
    }

    private static bool Test(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        var t = q / p;
        if (p < 0)
        {
            if (t > t1) return false;
            if (t > t0) t0 = t;
        }
        else
        {
            if (t < t0) return false;
            if (t < t1) t1 = t;
        }

        return true;
    }
}
