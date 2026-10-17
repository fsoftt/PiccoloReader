namespace PiccoloReader.Core.ViewModels;

// Maps the part of a zoomed/panned editor that is on screen back to page
// coordinates, so new annotations can be put where the user is looking.
public static class PageViewport
{
    // The part of the page inside the viewport, as a rectangle normalized to
    // the FULL page and limited to the visible crop. The page rectangle and
    // the viewport are in the same units and space (the editor's untransformed
    // local space); the page rectangle may be letterboxed inside the container
    // and extend beyond the viewport. Null when none of the page is in view.
    public static PageCrop? VisiblePart(
        double pageLeft, double pageTop, double pageWidth, double pageHeight,
        double viewLeft, double viewTop, double viewRight, double viewBottom,
        PageCrop crop)
    {
        if (pageWidth <= 0 || pageHeight <= 0)
        {
            return null;
        }

        var left = Math.Max(crop.Left, (viewLeft - pageLeft) / pageWidth);
        var top = Math.Max(crop.Top, (viewTop - pageTop) / pageHeight);
        var right = Math.Min(crop.Right, (viewRight - pageLeft) / pageWidth);
        var bottom = Math.Min(crop.Bottom, (viewBottom - pageTop) / pageHeight);

        return right > left && bottom > top ? new PageCrop(left, top, right, bottom) : null;
    }
}
