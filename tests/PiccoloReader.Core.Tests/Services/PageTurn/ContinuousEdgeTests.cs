using PiccoloReader.Core.Services.PageTurn;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Services.PageTurn;

public class ContinuousEdgeTests
{
    private const double Width = 100;
    private const double Spacing = 8;

    private static List<ContinuousPage> Pages(params double[] ratios) =>
        ratios.Select((r, i) => new ContinuousPage(i, r)).ToList();

    [Fact]
    public void Short_last_page_at_bottom_crosses_forward()
    {
        var pages = Pages(1.5, 1.5, 0.5); // tops 0,158,316; total 374
        var visible = 200.0;
        var scroll = 374 - visible; // bottom reached, last page top mid-screen
        var distance = ContinuousEdge.DistanceToEnd(pages, Width, Spacing, scroll, visible);
        Assert.Equal(0, distance);
        Assert.True(ContinuousEdge.ShouldCross(PageTurnIntent.Next, 100, distance));
        Assert.Null(ContinuousPageTurn.TargetOffset(pages, Width, Spacing, scroll, visible, PageTurnIntent.Next));
    }

    [Fact]
    public void Forward_never_scrolls_backward_when_end_is_above_view()
    {
        var pages = Pages(1.5, 1.5, 0.5);
        // Scrolled past the computed end (estimate mismatch).
        Assert.Null(ContinuousPageTurn.TargetOffset(pages, Width, Spacing, 200, 200, PageTurnIntent.Next));
    }

    [Fact]
    public void Tall_last_page_at_bottom_crosses_forward()
    {
        var pages = Pages(1.5, 3);
        var total = ContinuousLayout.PageTop(pages, 2, Width, Spacing);
        var distance = ContinuousEdge.DistanceToEnd(pages, Width, Spacing, total - 200, 200);
        Assert.True(ContinuousEdge.ShouldCross(PageTurnIntent.Next, 100, distance));
    }

    [Fact]
    public void Mid_document_does_not_cross()
    {
        var pages = Pages(1.5, 1.5, 0.5);
        var distance = ContinuousEdge.DistanceToEnd(pages, Width, Spacing, 50, 200);
        Assert.False(ContinuousEdge.ShouldCross(PageTurnIntent.Next, 50, distance));
        Assert.False(ContinuousEdge.ShouldCross(PageTurnIntent.Previous, 50, distance));
    }

    [Theory]
    [InlineData(1.9, true)]
    [InlineData(2, true)]
    [InlineData(2.5, false)]
    [InlineData(40, false)]
    public void Small_remainder_counts_as_end(double remaining, bool expected) =>
        Assert.Equal(expected, ContinuousEdge.ShouldCross(PageTurnIntent.Next, 100, remaining));

    [Fact]
    public void Zoomed_viewport_uses_visible_height()
    {
        var pages = Pages(1.5, 0.5); // total 216
        // Zoom 2 on a 400 tall list: 200 of content visible.
        Assert.Equal(0, ContinuousEdge.DistanceToEnd(pages, Width, Spacing, 16, 200));
        Assert.True(ContinuousEdge.DistanceToEnd(pages, Width, Spacing, 0, 200) > 0);
    }

    [Fact]
    public void Previous_crosses_only_at_start()
    {
        Assert.True(ContinuousEdge.ShouldCross(PageTurnIntent.Previous, 0, 500));
        Assert.True(ContinuousEdge.ShouldCross(PageTurnIntent.Previous, 1, 500));
        Assert.False(ContinuousEdge.ShouldCross(PageTurnIntent.Previous, 30, 500));
        Assert.False(ContinuousEdge.ShouldCross(PageTurnIntent.None, 0, 0));
    }
}
