using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services.PageTurn;

// Decides whether the continuous list is at the end (start) of the document,
// from how far it can still scroll, never from page alignment or the current
// page index: a short last page is fully visible long before its top is
// aligned with the viewport.
public static class ContinuousEdge
{
    // Platform scroll offsets are rounded to pixels and the scroll range is
    // an estimate, so a couple of dp of travel left still counts as the edge.
    public const double ToleranceDp = 2;

    public static bool IsAtEnd(double distanceToEndDp) => distanceToEndDp <= ToleranceDp;

    public static bool IsAtStart(double distanceToStartDp) => distanceToStartDp <= ToleranceDp;

    // True when a forward (backward) page turn or pull should open the
    // neighbouring sheet instead of scrolling.
    public static bool ShouldCross(PageTurnIntent intent, double distanceToStartDp, double distanceToEndDp) =>
        intent switch
        {
            PageTurnIntent.Next => IsAtEnd(distanceToEndDp),
            PageTurnIntent.Previous => IsAtStart(distanceToStartDp),
            _ => false,
        };

    // Content (dp) still below the visible part, from the page layout.
    public static double DistanceToEnd(
        IReadOnlyList<ContinuousPage> pages, double width, double spacing, double scrollTop, double visibleHeight) =>
        ContinuousLayout.PageTop(pages, pages.Count, width, spacing) - (scrollTop + visibleHeight);
}
