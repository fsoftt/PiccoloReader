using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

// Builds the duplicate made by the "Copy" menu of a figure: the same shape
// nudged a little so it is visibly a second one and can be dragged away.
public static class FigureCopy
{
    // Offset as a fraction of the page, on both axes.
    public const double Offset = 0.03;

    public static Annotation Copy(Annotation source, double left = 0, double top = 0, double right = 1, double bottom = 1)
    {
        var (x, y) = CopyPosition(source.X, source.Y, source.Width, source.Height, left, top, right, bottom);
        return new Annotation
        {
            SheetId = source.SheetId,
            PageIndex = source.PageIndex,
            Type = source.Type,
            IconKey = source.IconKey,
            X = x,
            Y = y,
            Width = source.Width,
            Height = source.Height,
            ColorHex = source.ColorHex,
            StrokeWidth = source.StrokeWidth,
            Points = source.Points,
            Text = source.Text,
            CoordinateSpace = source.CoordinateSpace,
            CreatedAt = DateTime.UtcNow
        };
    }

    // Moves the box down-right by Offset, or up-left when that would leave the
    // bounds; a box larger than the bounds stays where it is.
    public static (double X, double Y) CopyPosition(
        double x, double y, double width, double height, double left, double top, double right, double bottom) =>
        (Nudge(x, width, left, right), Nudge(y, height, top, bottom));

    private static double Nudge(double start, double size, double min, double max)
    {
        var forward = start + Offset;
        if (forward + size <= max)
        {
            return forward;
        }

        var backward = start - Offset;
        if (backward >= min)
        {
            return backward;
        }

        return Math.Clamp(forward, min, Math.Max(min, max - size));
    }
}
