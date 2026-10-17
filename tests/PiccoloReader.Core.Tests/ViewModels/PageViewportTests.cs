using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class PageViewportTests
{
    [Fact]
    public void VisiblePart_FullPageInView_IsTheWholePage()
    {
        var part = PageViewport.VisiblePart(0, 0, 100, 200, 0, 0, 100, 200, PageCrop.Full);

        Assert.Equal(PageCrop.Full, part);
    }

    [Fact]
    public void VisiblePart_ZoomedIntoBottomRight_IsThatQuarter()
    {
        var part = PageViewport.VisiblePart(0, 0, 100, 200, 50, 100, 100, 200, PageCrop.Full);

        Assert.Equal(new PageCrop(0.5, 0.5, 1, 1), part);
    }

    [Fact]
    public void VisiblePart_LetterboxedPage_IsRelativeToThePageNotTheContainer()
    {
        // Page 100 wide centered in a 300 wide container; viewport covers
        // the page's right half and the empty margin beyond it.
        var part = PageViewport.VisiblePart(100, 0, 100, 100, 150, 0, 300, 100, PageCrop.Full);

        Assert.Equal(new PageCrop(0.5, 0, 1, 1), part);
    }

    [Fact]
    public void VisiblePart_IsLimitedToTheCrop()
    {
        var crop = new PageCrop(0.25, 0.25, 0.75, 0.75);

        var part = PageViewport.VisiblePart(0, 0, 100, 100, 0, 0, 100, 100, crop);

        Assert.Equal(crop, part);
    }

    [Fact]
    public void VisiblePart_PageOutOfView_IsNull()
    {
        Assert.Null(PageViewport.VisiblePart(0, 0, 100, 100, 200, 0, 300, 100, PageCrop.Full));
    }
}
