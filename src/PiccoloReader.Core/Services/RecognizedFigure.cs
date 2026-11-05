using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

// Turns a recognized shape into the figure annotation that replaces the stroke.
public static class RecognizedFigure
{
    // The new (unsaved) annotation, moved to lie inside the visible crop, or
    // null when it does not fit there (the stroke is then kept as drawn).
    public static Annotation? Create(RecognizedShape shape, int sheetId, int pageIndex, string colorHex, PageCrop crop)
    {
        var area = crop.IsValid ? crop : PageCrop.Full;
        var annotation = new Annotation
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Type = AnnotationType.Icon,
            ColorHex = colorHex,
            CoordinateSpace = AnnotationCoordinateSpace.Page,
            CreatedAt = DateTime.UtcNow
        };

        switch (shape.Kind)
        {
            case RecognizedShapeKind.Slur when shape.ControlPoints is { Count: 4 } control:
                var (x, y, width, height, points) = SlurGeometry.FromPagePoints(control);
                annotation.IconKey = shape.Above ? SlurGeometry.AboveKey : SlurGeometry.BelowKey;
                annotation.X = x;
                annotation.Y = y;
                annotation.Width = width;
                annotation.Height = height;
                annotation.Points = AnnotationService.SerializePoints(points);
                break;

            case RecognizedShapeKind.Hairpin when shape.HairpinKey is { } key:
                annotation.IconKey = key;
                annotation.X = shape.X;
                annotation.Y = shape.Y;
                annotation.Width = shape.Width;
                annotation.Height = shape.Height;
                break;

            default:
                return null;
        }

        if (annotation.Width > area.Right - area.Left || annotation.Height > area.Bottom - area.Top)
        {
            return null;
        }

        annotation.X = Math.Clamp(annotation.X, area.Left, area.Right - annotation.Width);
        annotation.Y = Math.Clamp(annotation.Y, area.Top, area.Bottom - annotation.Height);
        return annotation;
    }

    // A stroke annotation that is not saved yet (what undo of a recognition
    // brings back).
    public static Annotation UnsavedStroke(int sheetId, int pageIndex, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points) =>
        new()
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Type = AnnotationType.Stroke,
            ColorHex = colorHex,
            StrokeWidth = strokeWidth,
            Points = AnnotationService.SerializePoints(points),
            CoordinateSpace = AnnotationCoordinateSpace.Page,
            CreatedAt = DateTime.UtcNow
        };
}
