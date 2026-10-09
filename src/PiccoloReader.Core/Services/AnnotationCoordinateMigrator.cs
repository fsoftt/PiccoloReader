using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

// Converts legacy annotations (normalized to the reading area container with
// the page letterboxed inside it) to page-normalized ones. The container the
// annotation was authored against is not stored; editing was effectively
// portrait-only, so a portrait reference container (short side as width,
// long side as height) built from the current reading area stands in for it.
public static class AnnotationCoordinateMigrator
{
    // Returns whether the annotation was converted (and so needs saving).
    // Page-space annotations, or an unusable page aspect / container, leave
    // it untouched.
    public static bool MigrateToPageSpace(Annotation annotation, double pageAspectRatio, double containerWidth, double containerHeight)
    {
        if (annotation.CoordinateSpace != AnnotationCoordinateSpace.Legacy
            || pageAspectRatio <= 0 || containerWidth <= 0 || containerHeight <= 0)
        {
            return false;
        }

        var frame = PageFrame.Fit(
            Math.Min(containerWidth, containerHeight),
            Math.Max(containerWidth, containerHeight),
            pageAspectRatio);

        if (annotation.IsStroke)
        {
            var points = AnnotationService.DeserializePoints(annotation.Points);
            if (points.Count > 0)
            {
                annotation.Points = AnnotationService.SerializePoints(
                    points.Select(p => new StrokePoint(frame.ToPageX(p.X), frame.ToPageY(p.Y))).ToList());
            }

            // Legacy width is a fraction of the container width.
            annotation.StrokeWidth /= frame.Width;
        }
        else
        {
            annotation.X = frame.ToPageX(annotation.X);
            annotation.Y = frame.ToPageY(annotation.Y);
            annotation.Width /= frame.Width;
            annotation.Height /= frame.Height;
        }

        annotation.CoordinateSpace = AnnotationCoordinateSpace.Page;
        return true;
    }
}
