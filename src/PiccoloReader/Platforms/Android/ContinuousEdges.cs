using AndroidX.RecyclerView.Widget;

namespace PiccoloReader.Platforms.Android;

// How far (dp) the continuous list can still scroll up and down, measured
// from the actual first/last page views. RecyclerView.CanScrollVertically is
// not usable for this: its scroll range is an estimate that is off by a
// page-ish amount when the last page is short.
internal static class ContinuousEdges
{
    private const double Far = 1e6;

    public static (double Above, double Below) Measure(RecyclerView rv, int pageCount, double zoom)
    {
        if (pageCount <= 0 || rv.GetLayoutManager() is not LinearLayoutManager lm)
        {
            return (Far, Far);
        }

        var density = rv.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        var above = Far;
        if (lm.FindViewByPosition(0) is { } first)
        {
            above = Math.Max(0, rv.PaddingTop - lm.GetDecoratedTop(first)) / density;
        }

        var below = Far;
        if (lm.FindViewByPosition(pageCount - 1) is { } last)
        {
            var visibleBottom = rv.PaddingTop + (rv.Height - rv.PaddingTop - rv.PaddingBottom) / Math.Max(1, zoom);
            below = Math.Max(0, lm.GetDecoratedBottom(last) - visibleBottom) / density;
        }

        return (above, below);
    }
}
