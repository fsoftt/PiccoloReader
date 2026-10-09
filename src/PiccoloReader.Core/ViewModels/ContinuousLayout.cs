namespace PiccoloReader.Core.ViewModels;

// Geometry of the continuous list: each page is `width * AspectRatio` tall
// (rounded, as the view sizes it) followed by `spacing`. Lets the view
// work out which page is at a given scroll offset - including while
// zoomed, when only part of the list's viewport is on screen - and where
// a page sits on screen, without asking the platform list.
public static class ContinuousLayout
{
    public static double PageHeight(ContinuousPage page, double width) => Math.Round(width * page.AspectRatio);

    public static double PageTop(IReadOnlyList<ContinuousPage> pages, int pageIndex, double width, double spacing)
    {
        var top = 0.0;
        for (var i = 0; i < pageIndex && i < pages.Count; i++)
        {
            top += PageHeight(pages[i], width) + spacing;
        }

        return top;
    }

    // Index of the page covering content offset y (spacing below a page
    // counts as that page). Clamped to the first/last page.
    public static int PageAt(IReadOnlyList<ContinuousPage> pages, double y, double width, double spacing)
    {
        if (pages.Count == 0)
        {
            return 0;
        }

        var top = 0.0;
        for (var i = 0; i < pages.Count; i++)
        {
            top += PageHeight(pages[i], width) + spacing;
            if (y < top)
            {
                return i;
            }
        }

        return pages.Count - 1;
    }
}
