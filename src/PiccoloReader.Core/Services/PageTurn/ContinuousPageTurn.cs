using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services.PageTurn;

// Where a pedal press scrolls the continuous list to. Works in content
// offsets (dp). `visibleHeight` is what is on screen (list height divided
// by the zoom), so a zoomed or tall page is read in overlapping chunks.
public static class ContinuousPageTurn
{
    public const double DefaultOverlap = 0.1;
    private const double Epsilon = 1;

    // Returns the new scroll offset, or null when there is nowhere to go
    // (already at the end / start).
    public static double? TargetOffset(
        IReadOnlyList<ContinuousPage> pages,
        double width,
        double spacing,
        double scrollTop,
        double visibleHeight,
        PageTurnIntent intent,
        double overlap = DefaultOverlap)
    {
        if (pages.Count == 0 || width <= 0 || visibleHeight <= 0 || intent == PageTurnIntent.None)
        {
            return null;
        }

        var step = visibleHeight * (1 - overlap);
        // Next must move a meaningful distance, never a few dp of dead
        // travel, unless the end of the list is what stops it.
        var minMove = step / 2;
        // Platform scroll offsets are rounded to whole pixels, so an offset
        // a hair above a page top still counts as being on that page.
        var current = ContinuousLayout.PageAt(pages, scrollTop + Epsilon, width, spacing);
        var top = ContinuousLayout.PageTop(pages, current, width, spacing);
        var pageBottom = top + ContinuousLayout.PageHeight(pages[current], width);
        if (current < pages.Count - 1 && scrollTop + Epsilon >= pageBottom)
        {
            // The viewport top sits in the gap below this page: it is
            // looking at the next one.
            current++;
            top = ContinuousLayout.PageTop(pages, current, width, spacing);
            pageBottom = top + ContinuousLayout.PageHeight(pages[current], width);
        }

        double target;

        if (intent == PageTurnIntent.Next)
        {
            if (pageBottom > scrollTop + visibleHeight + Epsilon)
            {
                // The rest of this page is below the viewport: next chunk.
                target = scrollTop + step;
            }
            else if (current < pages.Count - 1)
            {
                target = ContinuousLayout.PageTop(pages, current + 1, width, spacing);
                if (target - scrollTop < minMove)
                {
                    // Only a sliver of this page is left: scroll a full
                    // chunk instead (the content is continuous, so nothing
                    // is skipped).
                    target = scrollTop + step;
                }
            }
            else
            {
                return null;
            }
        }
        else if (scrollTop > top + Epsilon)
        {
            // Inside the current page: previous chunk, stopping at its top.
            target = Math.Max(scrollTop - step, top);
        }
        else if (current > 0)
        {
            // At the top of this page: show the end of the
            // previous one (its top when it fits on screen).
            var previousTop = ContinuousLayout.PageTop(pages, current - 1, width, spacing);
            var previousBottom = previousTop + ContinuousLayout.PageHeight(pages[current - 1], width);
            target = Math.Max(previousTop, previousBottom - visibleHeight);
        }
        else
        {
            return null;
        }

        // The list cannot scroll past its end: stop there, and report no move
        // (rather than a phantom one) when already at it.
        if (intent == PageTurnIntent.Next)
        {
            // Content ends after the last page's trailing spacing (and, zoomed, the
            // list footer that makes room for the scaled-away viewport part), so
            // the last scroll position is total - visibleHeight.
            var maxOffset = Math.Max(0, ContinuousLayout.PageTop(pages, pages.Count, width, spacing) - visibleHeight);
            target = Math.Min(target, maxOffset);
        }

        return Math.Abs(target - scrollTop) < 0.5 ? null : target;
    }
}
