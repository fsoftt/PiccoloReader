namespace PiccoloReader.Core.Services;

// Crescendo and decrescendo hairpins are drawn as two vector lines instead of
// a music-font glyph: the annotation's own Width/Height box is their length and
// opening, so stretching one lengthens the wedge without thickening its
// strokes (which stay a constant fraction of the page width).
public static class HairpinGeometry
{
    public const string CrescendoKey = "dynamicCrescendoHairpin";
    public const string DecrescendoKey = "dynamicDiminuendoHairpin";

    // Stroke thickness as a fraction of the page's width.
    public const double StrokeWidth = 0.003;

    public const double MinWidth = 0.06;
    public const double MaxWidth = 1.0;

    // Opening height limits, as a fraction of the page's width (the default
    // hairpin is 0.12 long and about 0.043 open).
    public const double MinOpening = 0.015;
    public const double MaxOpening = 0.08;

    public static bool IsHairpin(string? iconKey) => iconKey is CrescendoKey or DecrescendoKey;

    // Clamps a hairpin's box. Width and height are fractions of the page's own
    // width and height; pageAspectRatio is the page's height / width.
    public static (double Width, double Height) ClampSize(double width, double height, double pageAspectRatio)
    {
        var clampedWidth = Math.Clamp(width, MinWidth, MaxWidth);
        if (pageAspectRatio <= 0)
        {
            return (clampedWidth, height);
        }

        // Height is a page-height fraction, so in page widths it is Height * pageAspectRatio
        var opening = Math.Clamp(height * pageAspectRatio, MinOpening, MaxOpening);
        return (clampedWidth, opening / pageAspectRatio);
    }

    // The same, also keeping the hairpin inside the visible right edge (a page
    // coordinate): the length is capped at the space right of x, and when even
    // the minimum length doesn't fit x is shifted left.
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

    private static readonly (double X1, double Y1, double X2, double Y2)[] CrescendoLines =
        { (0.0, 0.5, 1.0, 0.0), (0.0, 0.5, 1.0, 1.0) };

    private static readonly (double X1, double Y1, double X2, double Y2)[] DecrescendoLines =
        { (0.0, 0.0, 1.0, 0.5), (0.0, 1.0, 1.0, 0.5) };

    // The two strokes as (x1, y1, x2, y2) fractions of the hairpin's box: a
    // crescendo opens to the right, a decrescendo closes to the right. The
    // arrays are shared, so callers must not modify them.
    public static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Lines(string iconKey) =>
        iconKey == DecrescendoKey ? DecrescendoLines : CrescendoLines;
}
