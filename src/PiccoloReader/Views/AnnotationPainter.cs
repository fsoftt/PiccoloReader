using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;

namespace PiccoloReader.Views;

// Draws annotations (strokes and music-font icons) onto a Skia canvas.
// Shared by the single-page viewer's overlay, the icon picker, and each
// page of the continuous reading mode so they all render identically.
// Annotation coordinates are normalized (0-1) to the page. A canvas covering
// just the page (the continuous list) maps them straight onto info; a canvas
// covering the reading area (the single-page viewer) passes the page's frame
// within it.
public class AnnotationPainter
{
    private readonly Dictionary<int, SKRect> _glyphBoundsCache = new();
    private readonly SKPaint _glyphPaint = new() { Color = SKColors.Black, IsAntialias = true };

    private readonly SKPaint _hairpinPaint = new()
    {
        Color = SKColors.Black,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        IsAntialias = true
    };

    public SKTypeface? Typeface { get; set; }

    // A crop clips the drawing to the visible part of the page: with it the
    // frame is the FULL page's, which extends past what is shown.
    public void DrawAnnotations(SKCanvas canvas, SKImageInfo info, IEnumerable<Annotation> annotations, PageFrame? pageFrame = null, PageCrop? crop = null) =>
        DrawAnnotations(canvas, info.Width, info.Height, annotations, pageFrame, crop);

    // The same for a canvas size that isn't a whole number of pixels: the sharp
    // zoom overlays draw a magnified part of the page by pretending the canvas
    // is the whole page area at a higher resolution.
    public void DrawAnnotations(SKCanvas canvas, float width, float height, IEnumerable<Annotation> annotations, PageFrame? pageFrame = null, PageCrop? crop = null)
    {
        var frame = pageFrame ?? PageFrame.Full;
        var restoreCount = canvas.Save();
        if (crop is { IsFull: false } visibleCrop)
        {
            var visible = frame.VisibleRegion(visibleCrop);
            canvas.ClipRect(new SKRect(
                (float)(visible.X * width),
                (float)(visible.Y * height),
                (float)((visible.X + visible.Width) * width),
                (float)((visible.Y + visible.Height) * height)));
        }

        foreach (var annotation in annotations)
        {
            if (annotation.IsStroke)
            {
                DrawStroke(canvas, annotation, width, height, frame);
                continue;
            }

            if (annotation.IsText)
            {
                if (!string.IsNullOrEmpty(annotation.Text))
                {
                    DrawText(canvas, annotation.Text, new SKRect(
                        (float)(frame.ToContainerX(annotation.X) * width),
                        (float)(frame.ToContainerY(annotation.Y) * height),
                        (float)(frame.ToContainerX(annotation.X + annotation.Width) * width),
                        (float)(frame.ToContainerY(annotation.Y + annotation.Height) * height)), ColorOf(annotation));
                }

                continue;
            }

            var icon = MusicIconCatalog.FindByKey(annotation.IconKey);
            if (icon is null)
            {
                continue;
            }

            var targetRect = new SKRect(
                (float)(frame.ToContainerX(annotation.X) * width),
                (float)(frame.ToContainerY(annotation.Y) * height),
                (float)(frame.ToContainerX(annotation.X + annotation.Width) * width),
                (float)(frame.ToContainerY(annotation.Y + annotation.Height) * height));

            if (HairpinGeometry.IsHairpin(annotation.IconKey))
            {
                _hairpinPaint.Color = ColorOf(annotation);
                DrawHairpin(canvas, annotation.IconKey, targetRect, (float)(HairpinGeometry.StrokeWidth * frame.Width * width));
                continue;
            }

            if (SlurGeometry.IsSlur(annotation.IconKey))
            {
                DrawSlur(canvas, SlurGeometry.ControlPoints(annotation), targetRect,
                    (float)Math.Max(SlurGeometry.MaxThickness * frame.Width * width, SlurGeometry.MinThicknessPx), ColorOf(annotation));
                continue;
            }

            DrawGlyphFitted(canvas, icon.Codepoint, targetRect, icon.VisualScale);
        }

        canvas.RestoreToCount(restoreCount);
    }

    // Text annotations use the system sans font; the box height is one line
    // (see TextFigure) and the text is stretched to the box width, so
    // resizing keeps it fitted like a glyph. Characters the main font lacks
    // (emoji, CJK, symbols) are drawn with a fallback font the system picks
    // for them, run by run. There is no complex shaping, so right-to-left
    // scripts are drawn in typing order and joining scripts are not joined.
    private const float MeasureFontSize = 100f;
    private static readonly SKTypeface TextTypeface = SKTypeface.Default;

    // One line's height for a font size of 1.
    private static readonly float LineHeightPerSize = LineHeightOf(TextTypeface);

    private static float LineHeightOf(SKTypeface typeface)
    {
        using var font = new SKFont(typeface, MeasureFontSize);
        var metrics = font.Metrics;
        var height = (metrics.Descent - metrics.Ascent) / MeasureFontSize;
        return height > 0 ? height : 1.2f;
    }

    // Fallback typefaces by code point; kept for the app's lifetime.
    private static readonly Dictionary<int, SKTypeface> FallbackTypefaces = new();

    private static SKTypeface TypefaceFor(int codePoint)
    {
        if (TextTypeface.ContainsGlyph(codePoint))
        {
            return TextTypeface;
        }

        lock (FallbackTypefaces)
        {
            if (!FallbackTypefaces.TryGetValue(codePoint, out var typeface))
            {
                typeface = SKFontManager.Default.MatchCharacter(codePoint) ?? TextTypeface;
                FallbackTypefaces[codePoint] = typeface;
            }

            return typeface;
        }
    }

    // The text split into runs of one typeface, by text element so an emoji
    // sequence or an accent stays together.
    private static List<(string Text, SKTypeface Typeface)> TextRuns(string text)
    {
        var runs = new List<(string Text, SKTypeface Typeface)>();
        var current = new System.Text.StringBuilder();
        SKTypeface? currentTypeface = null;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            var typeface = TypefaceFor(char.ConvertToUtf32(element, 0));
            if (currentTypeface is not null && !ReferenceEquals(typeface, currentTypeface))
            {
                runs.Add((current.ToString(), currentTypeface));
                current.Clear();
            }

            currentTypeface = typeface;
            current.Append(element);
        }

        if (currentTypeface is not null && current.Length > 0)
        {
            runs.Add((current.ToString(), currentTypeface));
        }

        return runs;
    }

    // One font for measuring and drawing a run: they must agree exactly (the
    // subpixel flag changes advances at small sizes), or the text drawn is
    // wider or narrower than the box measured for it.
    private static SKFont TextFontFor(SKTypeface typeface, float size) => new(typeface, size) { Subpixel = true };

    private static float MeasureRuns(List<(string Text, SKTypeface Typeface)> runs, float size)
    {
        float width = 0;
        foreach (var (text, typeface) in runs)
        {
            using var font = TextFontFor(typeface, size);
            width += font.MeasureText(text);
        }

        return width;
    }

    // The text's width / line height, as drawn: what TextFigure.Size needs.
    public static double MeasureTextAspect(string text) =>
        MeasureRuns(TextRuns(text), MeasureFontSize) / (LineHeightPerSize * MeasureFontSize);

    private static void DrawText(SKCanvas canvas, string text, SKRect rect, SKColor color)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || text.Length == 0)
        {
            return;
        }

        var size = rect.Height / LineHeightPerSize;
        var runs = TextRuns(text);
        var measured = MeasureRuns(runs, size);
        if (measured <= 0)
        {
            return;
        }

        using var baseFont = new SKFont(TextTypeface, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var restore = canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        canvas.Scale(rect.Width / measured, 1);
        var x = 0f;
        foreach (var (run, typeface) in runs)
        {
            using var font = TextFontFor(typeface, size);
            canvas.DrawText(run, x, -baseFont.Metrics.Ascent, font, paint);
            x += font.MeasureText(run);
        }

        canvas.RestoreToCount(restore);
    }

    // A slur is a filled crescent: two Bezier edges from one end to the other,
    // pushed apart in the middle (see SlurGeometry.Edges), so its tips are
    // thin and its belly is thickest. Control points are box fractions.
    public void DrawSlur(SKCanvas canvas, IReadOnlyList<StrokePoint> controlPoints, SKRect rect, float thickness, SKColor color)
    {
        if (controlPoints.Count != 4 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var pixels = controlPoints
            .Select(p => (X: (double)rect.Left + p.X * rect.Width, Y: (double)rect.Top + p.Y * rect.Height))
            .ToArray();
        var (outer, inner) = SlurGeometry.Edges(pixels, thickness);

        using var path = new SKPath();
        path.MoveTo((float)outer[0].X, (float)outer[0].Y);
        path.CubicTo((float)outer[1].X, (float)outer[1].Y, (float)outer[2].X, (float)outer[2].Y, (float)outer[3].X, (float)outer[3].Y);
        path.LineTo((float)inner[3].X, (float)inner[3].Y);
        path.CubicTo((float)inner[2].X, (float)inner[2].Y, (float)inner[1].X, (float)inner[1].Y, (float)inner[0].X, (float)inner[0].Y);
        path.Close();

        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(path, paint);
    }

    private static SKColor ColorOf(Annotation annotation) =>
        annotation.ColorHex is { } hex && SKColor.TryParse(hex, out var color) ? color : SKColors.Black;

    // Hairpins fill their whole box with two lines, so a stretched one gets
    // longer while its stroke thickness stays the same.
    private void DrawHairpin(SKCanvas canvas, string iconKey, SKRect rect, float strokeWidth)
    {
        _hairpinPaint.StrokeWidth = strokeWidth;
        var inset = strokeWidth / 2;
        var box = new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        foreach (var (x1, y1, x2, y2) in HairpinGeometry.Lines(iconKey))
        {
            canvas.DrawLine(
                box.Left + (float)x1 * box.Width, box.Top + (float)y1 * box.Height,
                box.Left + (float)x2 * box.Width, box.Top + (float)y2 * box.Height,
                _hairpinPaint);
        }
    }

    private static void DrawStroke(SKCanvas canvas, Annotation annotation, float width, float height, PageFrame frame)
    {
        if (annotation.ColorHex is null)
        {
            return;
        }

        DrawStroke(canvas, AnnotationService.DeserializePoints(annotation.Points), annotation.ColorHex, annotation.StrokeWidth, width, height, frame);
    }

    // The stroke being drawn right now (not saved yet), painted like a saved
    // one so the final result does not jump.
    public void DrawLiveStroke(SKCanvas canvas, float width, float height, IReadOnlyList<StrokePoint> points, string colorHex, double strokeWidth, PageFrame? pageFrame = null) =>
        DrawStroke(canvas, points, colorHex, strokeWidth, width, height, pageFrame ?? PageFrame.Full);

    private static void DrawStroke(SKCanvas canvas, IReadOnlyList<StrokePoint> points, string colorHex, double strokeWidth, float width, float height, PageFrame frame)
    {
        if (points.Count < 2)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)(frame.ToContainerX(points[0].X) * width), (float)(frame.ToContainerY(points[0].Y) * height));
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo((float)(frame.ToContainerX(points[i].X) * width), (float)(frame.ToContainerY(points[i].Y) * height));
        }

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(colorHex),
            StrokeWidth = (float)(strokeWidth * frame.Width * width),
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };

        canvas.DrawPath(path, paint);
    }

    // Fits a glyph tightly to targetRect instead of relying on the font's
    // own em-box sizing - Bravura (like most music fonts) reserves a lot
    // of vertical em-box space for staff-line alignment around each
    // glyph's actual ink, so naively scaling a font size leaves the
    // *displayed* glyph the same apparent size regardless (confirmed by
    // testing FontImageSource.Size 28 through 72 on a clean install with
    // zero visible difference). Measuring the glyph's own ink bounds via
    // Skia and fitting *that* to targetRect sidesteps it entirely.
    // Shared by the icon picker buttons and the on-page annotation
    // overlay so both render identically.
    //
    // A dragged icon's move/resize repaints this every frame (AnnotationCanvas
    // is invalidated on every PanUpdated "Running" event), so this used to
    // allocate two SKFont objects and run MeasureText twice per icon per
    // frame - on the emulator's software rendering that was slow enough to
    // visibly drop frames mid-drag (reported as "shaky" move/resize).
    // measuredBounds only depends on the typeface+codepoint, never on
    // targetRect, so it's cached once per codepoint instead of remeasured
    // every repaint; _glyphPaint is similarly a single reused instance
    // instead of a fresh allocation per glyph per frame.
    public void DrawGlyphFitted(SKCanvas canvas, int codepoint, SKRect targetRect, double visualScale = 1.0)
    {
        if (Typeface is null || targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return;
        }

        var text = char.ConvertFromUtf32(codepoint);

        if (!_glyphBoundsCache.TryGetValue(codepoint, out var measuredBounds))
        {
            using var measureFont = new SKFont(Typeface, 100);
            measureFont.MeasureText(text, out measuredBounds);
            _glyphBoundsCache[codepoint] = measuredBounds;
        }

        if (measuredBounds.Width <= 0 || measuredBounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(targetRect.Width / measuredBounds.Width, targetRect.Height / measuredBounds.Height) * 0.9f * (float)visualScale;
        using var font = new SKFont(Typeface, 100f * scale);
        font.MeasureText(text, out var fittedBounds);

        var x = targetRect.MidX - fittedBounds.MidX;
        var y = targetRect.MidY - fittedBounds.MidY;

        canvas.DrawText(text, x, y, font, _glyphPaint);
    }
}
