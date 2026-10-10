using System.Drawing;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>W3C pointer-action gestures (tap, long-press, drag, polyline, two-finger pinch).</summary>
public static class Gestures
{
    private static void Perform(params ActionSequence[] sequences) =>
        ((IActionExecutor)AppSession.Driver).PerformActions(sequences.ToList());

    private static Point At(Rectangle r, double fx, double fy) =>
        new(r.X + (int)(r.Width * fx), r.Y + (int)(r.Height * fy));

    /// <summary>Point inside an element given as fractions (0..1) of its bounds.</summary>
    public static Point PointIn(IWebElement element, double fx, double fy) =>
        At(new Rectangle(element.Location, element.Size), fx, fy);

    public static void Tap(Point p)
    {
        var finger = new PointerInputDevice(PointerKind.Touch, "finger");
        var seq = new ActionSequence(finger);
        seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, p.X, p.Y, TimeSpan.Zero));
        seq.AddAction(finger.CreatePointerDown(MouseButton.Left));
        seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(60)));
        seq.AddAction(finger.CreatePointerUp(MouseButton.Left));
        Perform(seq);
    }

    public static void TapIn(IWebElement element, double fx, double fy) => Tap(PointIn(element, fx, fy));

    public static void LongPress(Point p, int holdMs = 1500)
    {
        var finger = new PointerInputDevice(PointerKind.Touch, "finger");
        var seq = new ActionSequence(finger);
        seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, p.X, p.Y, TimeSpan.Zero));
        seq.AddAction(finger.CreatePointerDown(MouseButton.Left));
        seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(holdMs)));
        seq.AddAction(finger.CreatePointerUp(MouseButton.Left));
        Perform(seq);
    }

    public static void LongPress(IWebElement element, int holdMs = 1500) =>
        LongPress(PointIn(element, 0.5, 0.5), holdMs);

    public static void Drag(Point from, Point to, int durationMs = 700) =>
        Polyline(new[] { from, to }, durationMs);

    /// <summary>
    /// One finger down at the first point, dragged through the rest (each segment taking
    /// <paramref name="msPerSegment"/>), then lifted. Used for strokes and erasing sweeps.
    /// </summary>
    public static void Polyline(IReadOnlyList<Point> points, int msPerSegment)
    {
        var finger = new PointerInputDevice(PointerKind.Touch, "finger");
        var seq = new ActionSequence(finger);
        seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, points[0].X, points[0].Y, TimeSpan.Zero));
        seq.AddAction(finger.CreatePointerDown(MouseButton.Left));
        seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(150)));
        foreach (var p in points.Skip(1))
        {
            seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, p.X, p.Y, TimeSpan.FromMilliseconds(msPerSegment)));
        }

        seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(100)));
        seq.AddAction(finger.CreatePointerUp(MouseButton.Left));
        Perform(seq);
    }

    /// <summary>
    /// Two-finger pinch around <paramref name="center"/> along the horizontal axis. Fingers go from
    /// <paramref name="startHalfSpan"/> to <paramref name="endHalfSpan"/> px from the center
    /// (end &gt; start zooms in, end &lt; start zooms out).
    /// </summary>
    public static void Pinch(Point center, int startHalfSpan, int endHalfSpan, int durationMs = 800)
    {
        ActionSequence Finger(string name, int sign)
        {
            var finger = new PointerInputDevice(PointerKind.Touch, name);
            var seq = new ActionSequence(finger);
            seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, center.X + sign * startHalfSpan, center.Y, TimeSpan.Zero));
            seq.AddAction(finger.CreatePointerDown(MouseButton.Left));
            seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(100)));
            seq.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, center.X + sign * endHalfSpan, center.Y, TimeSpan.FromMilliseconds(durationMs)));
            seq.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(100)));
            seq.AddAction(finger.CreatePointerUp(MouseButton.Left));
            return seq;
        }

        Perform(Finger("finger1", -1), Finger("finger2", +1));
    }
}
