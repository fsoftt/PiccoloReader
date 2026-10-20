using PiccoloReader.Core.Services.PageTurn;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Services.PageTurn;

public class PagedViewportTests
{
    [Fact]
    public void BaseScale_is_one_in_portrait()
    {
        Assert.Equal(1, PagedViewport.BaseScale(400, 800, 1.4, PageCrop.Full, 5));
    }

    [Fact]
    public void BaseScale_fills_width_in_landscape()
    {
        var expected = 800 / (400 / 1.4);
        Assert.Equal(expected, PagedViewport.BaseScale(800, 400, 1.4, PageCrop.Full, 5), 6);
    }

    [Fact]
    public void BaseScale_is_one_when_page_is_already_full_width()
    {
        Assert.Equal(1, PagedViewport.BaseScale(800, 400, 0.4, PageCrop.Full, 5));
    }

    [Fact]
    public void BaseScale_uses_cropped_width()
    {
        var crop = new PageCrop(0.25, 0, 0.75, 1);
        var expected = 800 / (400 / 2.8);
        Assert.Equal(expected, PagedViewport.BaseScale(800, 400, 1.4, crop, 10), 6);
    }

    [Fact]
    public void BaseScale_is_capped()
    {
        Assert.Equal(2, PagedViewport.BaseScale(800, 400, 1.4, PageCrop.Full, 2));
    }

    [Fact]
    public void Top_alignment_at_base_scale_puts_page_top_at_zero_and_locks_x()
    {
        var pw = 400 / 1.4;
        var start = (800 - pw) / 2;
        var end = start + pw;
        var scale = 800 / pw;
        Assert.Equal(0, PagedViewport.AlignY(400, scale, 0, 400, false, true), 6);
        var (min, max) = PagedViewport.Range(800, scale, start, end, true);
        Assert.Equal(min, max, 6);
        Assert.Equal(-start * scale, max, 6);
    }

    [Fact]
    public void Bottom_alignment_shows_page_bottom()
    {
        Assert.Equal(-400, PagedViewport.AlignY(400, 2, 0, 400, true, true), 6);
        Assert.Equal(0, PagedViewport.AlignY(400, 2, 0, 400, false, true), 6);
    }

    [Fact]
    public void Clamp_without_fit_uses_container_bounds()
    {
        Assert.Equal(-300, PagedViewport.Clamp(-500, 300, 2, 50, 250, false));
        Assert.Equal(0, PagedViewport.Clamp(40, 300, 2, 50, 250, false));
    }

    [Fact]
    public void Clamp_with_fit_stays_on_page()
    {
        Assert.Equal(-400, PagedViewport.Clamp(-900, 400, 2, 0, 400, true));
        Assert.Equal(0, PagedViewport.Clamp(30, 400, 2, 0, 400, true));
    }

    [Fact]
    public void Fit_falls_back_to_container_bounds_when_page_smaller()
    {
        Assert.Equal((-400.0, 0.0), PagedViewport.Range(400, 2, 0, 100, true));
    }

    [Fact]
    public void Pedal_without_overflow_does_nothing()
    {
        var r = PagedViewport.PedalTurn(0, 400, 1, 0, 400, PageTurnIntent.Next);
        Assert.Equal(PagedScrollAction.None, r.Action);
    }

    [Fact]
    public void Pedal_next_scrolls_by_viewport_minus_overlap_then_clamps_then_turns()
    {
        var r = PagedViewport.PedalTurn(0, 400, 3, 0, 400, PageTurnIntent.Next);
        Assert.Equal(PagedScrollAction.ScrollTo, r.Action);
        Assert.Equal(-360, r.Translation, 6);

        r = PagedViewport.PedalTurn(-700, 400, 3, 0, 400, PageTurnIntent.Next);
        Assert.Equal(-800, r.Translation, 6);

        r = PagedViewport.PedalTurn(-800, 400, 3, 0, 400, PageTurnIntent.Next);
        Assert.Equal(PagedScrollAction.NextPage, r.Action);
    }

    [Fact]
    public void Pedal_previous_scrolls_up_then_turns()
    {
        var r = PagedViewport.PedalTurn(-800, 400, 3, 0, 400, PageTurnIntent.Previous);
        Assert.Equal(PagedScrollAction.ScrollTo, r.Action);
        Assert.Equal(-440, r.Translation, 6);

        r = PagedViewport.PedalTurn(-300, 400, 3, 0, 400, PageTurnIntent.Previous);
        Assert.Equal(0, r.Translation, 6);

        r = PagedViewport.PedalTurn(0, 400, 3, 0, 400, PageTurnIntent.Previous);
        Assert.Equal(PagedScrollAction.PreviousPage, r.Action);
    }
}
