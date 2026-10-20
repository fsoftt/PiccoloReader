using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services.PageTurn;

public enum PagedScrollAction
{
    None,
    ScrollTo,
    NextPage,
    PreviousPage,
}

public readonly record struct PagedScrollResult(PagedScrollAction Action, double Translation);

// Zoom/pan math of the paged reading modes. The page view is scaled around
// its top-left corner (anchor 0,0) by `scale`, so a local point p is drawn
// at `translation + p * scale`. `start`/`end` are the visible page's extent
// along one axis in the view's unscaled local units.
public static class PagedViewport
{
    public const double DefaultOverlap = 0.1;
    private const double Epsilon = 1;

    // In landscape the fitted page is narrower than the screen: scale it up
    // so it spans the full width. 1 otherwise.
    public static double BaseScale(double containerWidth, double containerHeight, double aspectRatio, PageCrop crop, double maxScale)
    {
        if (containerWidth <= containerHeight || containerHeight <= 0 || aspectRatio <= 0)
        {
            return 1;
        }

        var frame = PageFrame.Fit(containerWidth, containerHeight, aspectRatio, crop).VisibleRegion(crop);
        var pageWidth = frame.Width * containerWidth;
        if (pageWidth <= 0 || pageWidth >= containerWidth - 0.5)
        {
            return 1;
        }

        return Math.Min(containerWidth / pageWidth, maxScale);
    }

    // Allowed translation range along an axis. With fitToExtent, a page that
    // overflows the container is never dragged past its own edges (no empty
    // bands) and one that is smaller than the container is centered and
    // locked; without it the whole container may be panned (min..0).
    public static (double Min, double Max) Range(double container, double scale, double start, double end, bool fitToExtent)
    {
        if (fitToExtent)
        {
            if ((end - start) * scale >= container - 0.5)
            {
                var min = container - end * scale;
                var max = -start * scale;
                return min <= max ? (min, max) : (max, max);
            }

            var centered = (container - (start + end) * scale) / 2;
            return (centered, centered);
        }

        return (Math.Min(0, -container * (scale - 1)), 0);
    }

    public static double Clamp(double translation, double container, double scale, double start, double end, bool fitToExtent)
    {
        var (min, max) = Range(container, scale, start, end, fitToExtent);
        return Math.Clamp(translation, min, max);
    }

    // Translation that puts the page's top (or bottom) edge at the top
    // (bottom) of the container, clamped to the allowed range.
    public static double AlignY(double container, double scale, double start, double end, bool bottom, bool fitToExtent)
    {
        var target = bottom ? container - end * scale : -start * scale;
        return Clamp(target, container, scale, start, end, fitToExtent);
    }

    // Zoom kept across a page turn, relative to the page base scale: null
    // when the view is at (or below) the base, i.e. nothing to carry.
    public static double? CarryRatio(double currentScale, double baseScale, double threshold)
    {
        if (baseScale <= 0)
        {
            return null;
        }

        var ratio = currentScale / baseScale;
        return ratio > threshold ? ratio : null;
    }

    // The scale on the new page for a carried ratio.
    public static double CarryScale(double newBaseScale, double ratio, double maxScale) =>
        Math.Clamp(newBaseScale * ratio, 1, maxScale);

    // Where the view sits within its pan range: 0 = left/top edge of the
    // range (translation at max), 1 = right/bottom edge (translation at min).
    // A range with no room is centered.
    public static double PanFraction(double translation, double min, double max)
    {
        if (max - min <= 0.5)
        {
            return 0.5;
        }

        return Math.Clamp((max - translation) / (max - min), 0, 1);
    }

    public static double TranslationFromFraction(double fraction, double min, double max) =>
        max - Math.Clamp(fraction, 0, 1) * (max - min);

    // Whether the page overflows the container along the axis.
    public static bool Overflows(double container, double scale, double start, double end) =>
        (end - start) * scale > container + Epsilon;

    // A drag on the mode's axis (negative = towards the next page). Without
    // room to pan on that axis any drag past the threshold turns the page;
    // with room, only a drag that started at the pan limit in its direction
    // does (translation is the one at the start of the drag).
    public static PageTurnIntent SwipeTurn(double drag, double startTranslation, double min, double max, double threshold)
    {
        var hasRoom = max - min > 2;
        if (drag <= -threshold && (!hasRoom || startTranslation <= min + Epsilon))
        {
            return PageTurnIntent.Next;
        }

        if (drag >= threshold && (!hasRoom || startTranslation >= max - Epsilon))
        {
            return PageTurnIntent.Previous;
        }

        return PageTurnIntent.None;
    }

    // Pedal press on a paged view whose page overflows vertically: scroll by a
    // viewport (minus overlap) or, at the page's end/start, turn the page.
    // Action None means no vertical overflow: the caller turns the page.
    public static PagedScrollResult PedalTurn(
        double translationY, double containerHeight, double scale, double start, double end,
        PageTurnIntent intent, double overlap = DefaultOverlap)
    {
        if (intent == PageTurnIntent.None || !Overflows(containerHeight, scale, start, end))
        {
            return new PagedScrollResult(PagedScrollAction.None, translationY);
        }

        var step = containerHeight * (1 - overlap);
        var top = translationY + start * scale;
        var bottom = translationY + end * scale;
        if (intent == PageTurnIntent.Next)
        {
            if (bottom > containerHeight + Epsilon)
            {
                var target = Math.Max(translationY - step, containerHeight - end * scale);
                return new PagedScrollResult(PagedScrollAction.ScrollTo, target);
            }

            return new PagedScrollResult(PagedScrollAction.NextPage, translationY);
        }

        if (top < -Epsilon)
        {
            var target = Math.Min(translationY + step, -start * scale);
            return new PagedScrollResult(PagedScrollAction.ScrollTo, target);
        }

        return new PagedScrollResult(PagedScrollAction.PreviousPage, translationY);
    }
}
