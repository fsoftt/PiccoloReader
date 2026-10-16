using PiccoloReader.Core.Services.PageTurn;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Services.PageTurn;

public class ContinuousPageTurnTests
{
    private const double Width = 100;
    private const double Spacing = 8;

    // Page height = Width * ratio; tops at 0, then +height+spacing.
    private static List<ContinuousPage> Pages(params double[] ratios) =>
        ratios.Select((r, i) => new ContinuousPage(i, r)).ToList();

    private static double? Target(List<ContinuousPage> pages, double scroll, double visible, PageTurnIntent intent) =>
        ContinuousPageTurn.TargetOffset(pages, Width, Spacing, scroll, visible, intent);

    [Fact]
    public void Short_pages_next_aligns_next_page_top()
    {
        var pages = Pages(1, 1, 1); // 100 tall, tops 0,108,216
        Assert.Equal(108, Target(pages, 0, 100, PageTurnIntent.Next));
        Assert.Equal(216, Target(pages, 108, 100, PageTurnIntent.Next));
    }

    [Fact]
    public void Tall_page_next_scrolls_one_viewport_minus_overlap()
    {
        var pages = Pages(3, 1); // 300 tall
        Assert.Equal(90, Target(pages, 0, 100, PageTurnIntent.Next));
        Assert.Equal(180, Target(pages, 90, 100, PageTurnIntent.Next));
    }

    [Fact]
    public void Tall_page_next_moves_on_once_its_bottom_is_visible()
    {
        var pages = Pages(3, 1);
        Assert.Equal(308, Target(pages, 200, 100, PageTurnIntent.Next));
        Assert.Equal(316, Target(pages, 270, 100, PageTurnIntent.Next)); // sliver left: full chunk, clamped to the end
    }

    [Fact]
    public void Last_page_next_does_nothing()
    {
        var pages = Pages(1, 1);
        Assert.Null(Target(pages, 108, 300, PageTurnIntent.Next));
    }

    [Fact]
    public void First_page_top_previous_does_nothing()
    {
        var pages = Pages(1, 1);
        Assert.Null(Target(pages, 0, 300, PageTurnIntent.Previous));
    }

    [Fact]
    public void Previous_from_inside_page_goes_to_its_top()
    {
        var pages = Pages(1, 1);
        Assert.Equal(108, Target(pages, 150, 300, PageTurnIntent.Previous));
    }

    [Fact]
    public void Previous_at_page_top_shows_previous_short_page_top()
    {
        var pages = Pages(1, 1, 1);
        Assert.Equal(0, Target(pages, 108, 300, PageTurnIntent.Previous));
    }

    [Fact]
    public void Previous_at_page_top_shows_end_of_previous_tall_page()
    {
        var pages = Pages(3, 1); // page 0: 0..300, page 1 top 308
        Assert.Equal(200, Target(pages, 308, 100, PageTurnIntent.Previous));
    }

    [Fact]
    public void Previous_inside_tall_page_scrolls_back_one_chunk()
    {
        var pages = Pages(3, 1);
        Assert.Equal(110, Target(pages, 200, 100, PageTurnIntent.Previous));
        Assert.Equal(0, Target(pages, 50, 100, PageTurnIntent.Previous));
    }

    [Fact]
    public void Zoom_shrinks_visible_height_so_short_page_is_read_in_chunks()
    {
        var pages = Pages(1.5, 1.5); // 150 tall; zoom 2 on a 200 list = 100 visible
        Assert.Equal(90, Target(pages, 0, 100, PageTurnIntent.Next));
        Assert.Equal(158, Target(pages, 90, 100, PageTurnIntent.Next));
    }

    [Fact]
    public void Next_never_scrolls_past_the_end_of_the_list()
    {
        var pages = Pages(1, 1, 1); // total 324, viewport 300: max offset 24
        Assert.Equal(24, Target(pages, 0, 300, PageTurnIntent.Next));
        Assert.Null(Target(pages, 24, 300, PageTurnIntent.Next));
    }

    [Fact]
    public void Offset_a_hair_above_a_page_top_counts_as_on_that_page()
    {
        var pages = Pages(1, 1, 1); // tops 0,108,216
        Assert.Equal(216, Target(pages, 107.9, 100, PageTurnIntent.Next));
        Assert.Equal(0, Target(pages, 107.9, 100, PageTurnIntent.Previous));
    }

    [Fact]
    public void Empty_or_invalid_input_returns_null()
    {
        Assert.Null(Target(new List<ContinuousPage>(), 0, 100, PageTurnIntent.Next));
        Assert.Null(Target(Pages(1), 0, 0, PageTurnIntent.Next));
        Assert.Null(Target(Pages(1, 1), 0, 100, PageTurnIntent.None));
    }

    [Fact]
    public void Offset_in_the_gap_below_a_page_counts_as_the_next_page()
    {
        var pages = Pages(1, 1, 1); // page 0 ends at 100, page 1 starts at 108
        // Next is a real chunk, not a 4dp hop to the page top.
        Assert.Equal(194, Target(pages, 104, 100, PageTurnIntent.Next));
        Assert.Equal(0, Target(pages, 104, 100, PageTurnIntent.Previous));
    }

    [Fact]
    public void Next_with_only_a_sliver_of_the_page_left_scrolls_a_full_chunk()
    {
        var pages = Pages(3, 1, 1); // tops 0,308,416; max offset 524-100
        Assert.Equal(380, Target(pages, 290, 100, PageTurnIntent.Next));
    }

    [Fact]
    public void Next_reaches_the_true_end_including_the_trailing_spacing_and_stops()
    {
        var pages = Pages(1, 1); // content 216 (trailing spacing included), viewport 150
        Assert.Equal(66, Target(pages, 0, 150, PageTurnIntent.Next));
        Assert.Null(Target(pages, 66, 150, PageTurnIntent.Next));
    }

    [Fact]
    public void Zoomed_in_next_walks_the_whole_list_in_overlapping_chunks()
    {
        // Zoom 2 on a 400 list: 200 of content visible, pages 250 tall.
        var pages = Pages(2.5, 2.5, 2.5); // tops 0,258,516; content 774; max offset 574
        const double visible = 200;
        double scroll = 0;
        var presses = 0;
        while (Target(pages, scroll, visible, PageTurnIntent.Next) is { } next && presses < 20)
        {
            Assert.True(next - scroll >= visible * 0.9 / 2 || next == 574, $"dead move {scroll}->{next}");
            Assert.True(next <= scroll + visible + Spacing, $"skipped content {scroll}->{next}");
            scroll = next;
            presses++;
        }

        Assert.Equal(574, scroll);
        Assert.True(presses < 20);
    }

    [Fact]
    public void Zoomed_in_previous_walks_back_to_the_top_without_skipping()
    {
        var pages = Pages(2.5, 2.5, 2.5);
        const double visible = 200;
        double scroll = 574;
        var presses = 0;
        while (Target(pages, scroll, visible, PageTurnIntent.Previous) is { } prev && presses < 20)
        {
            Assert.True(prev < scroll);
            Assert.True(prev >= scroll - visible - Spacing, $"skipped content {scroll}->{prev}");
            scroll = prev;
            presses++;
        }

        Assert.Equal(0, scroll);
        Assert.True(presses < 20);
    }
}
