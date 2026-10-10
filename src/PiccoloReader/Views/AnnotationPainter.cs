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

    public SKTypeface? Typeface { get; set; }

    // A crop clips the drawing to the visible part of the page: with it the
    // frame is the FULL page's, which extends past what is shown.
    public void DrawAnnotations(SKCanvas canvas, SKImageInfo info, IEnumerable<Annotation> annotations, PageFrame? pageFrame = null, PageCrop? crop = null)
    {
        var frame = pageFrame ?? PageFrame.Full;
        var restoreCount = canvas.Save();
        if (crop is { IsFull: false } visibleCrop)
        {
            var visible = frame.VisibleRegion(visibleCrop);
            canvas.ClipRect(new SKRect(
                (float)(visible.X * info.Width),
                (float)(visible.Y * info.Height),
                (float)((visible.X + visible.Width) * info.Width),
                (float)((visible.Y + visible.Height) * info.Height)));
        }

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
                (float)(frame.ToContainerX(annotation.X) * info.Width),
                (float)(frame.ToContainerY(annotation.Y) * info.Height),
                (float)(frame.ToContainerX(annotation.X + annotation.Width) * info.Width),
                (float)(frame.ToContainerY(annotation.Y + annotation.Height) * info.Height));

            DrawGlyphFitted(canvas, icon.Codepoint, targetRect, icon.VisualScale);
        }

        canvas.RestoreToCount(restoreCount);
    }

    private static void DrawStroke(SKCanvas canvas, Annotation annotation, SKImageInfo info, PageFrame frame)
    {
        var points = AnnotationService.DeserializePoints(annotation.Points);
        if (points.Count < 2 || annotation.ColorHex is null)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)(frame.ToContainerX(points[0].X) * info.Width), (float)(frame.ToContainerY(points[0].Y) * info.Height));
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo((float)(frame.ToContainerX(points[i].X) * info.Width), (float)(frame.ToContainerY(points[i].Y) * info.Height));
        }

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(annotation.ColorHex),
            StrokeWidth = (float)(annotation.StrokeWidth * frame.Width * info.Width),
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
