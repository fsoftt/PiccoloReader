using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;

namespace PiccoloReader.Views;

// Draws annotations (strokes and music-font icons) onto a Skia canvas.
// Shared by the single-page viewer's overlay, the icon picker, and each
// page of the continuous reading mode so they all render identically.
// Annotation coordinates are normalized (0-1) to the single-page viewer's
// container, so by default info's size is that container's size. A canvas
// covering just the page (the continuous list) passes the page's frame
// within the container to map them onto itself.
public class AnnotationPainter
{
    private readonly Dictionary<int, SKRect> _glyphBoundsCache = new();
    private readonly SKPaint _glyphPaint = new() { Color = SKColors.Black, IsAntialias = true };

    public SKTypeface? Typeface { get; set; }

    public void DrawAnnotations(SKCanvas canvas, SKImageInfo info, IEnumerable<Annotation> annotations, PageFrame? pageFrame = null)
    {
        var frame = pageFrame ?? PageFrame.Full;
        foreach (var annotation in annotations)
        {
            if (annotation.IsStroke)
            {
                DrawStroke(canvas, annotation, info, frame);
                continue;
            }

            var icon = MusicIconCatalog.FindByKey(annotation.IconKey);
            if (icon is null)
            {
                continue;
            }

            var targetRect = new SKRect(
                (float)(frame.ToPageX(annotation.X) * info.Width),
                (float)(frame.ToPageY(annotation.Y) * info.Height),
                (float)(frame.ToPageX(annotation.X + annotation.Width) * info.Width),
                (float)(frame.ToPageY(annotation.Y + annotation.Height) * info.Height));

            DrawGlyphFitted(canvas, icon.Codepoint, targetRect, icon.VisualScale);
        }
    }

    private static void DrawStroke(SKCanvas canvas, Annotation annotation, SKImageInfo info, PageFrame frame)
    {
        var points = AnnotationService.DeserializePoints(annotation.Points);
        if (points.Count < 2 || annotation.ColorHex is null)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)(frame.ToPageX(points[0].X) * info.Width), (float)(frame.ToPageY(points[0].Y) * info.Height));
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo((float)(frame.ToPageX(points[i].X) * info.Width), (float)(frame.ToPageY(points[i].Y) * info.Height));
        }

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(annotation.ColorHex),
            StrokeWidth = (float)(annotation.StrokeWidth / frame.Width * info.Width),
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
