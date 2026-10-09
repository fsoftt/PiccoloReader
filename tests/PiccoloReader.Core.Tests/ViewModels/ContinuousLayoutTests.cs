using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class ContinuousLayoutTests
{
    // Width 100, spacing 10: pages are 150, 100, 200 tall ->
    // page 0 spans [0,160), page 1 [160,270), page 2 [270,480).
    private static readonly IReadOnlyList<ContinuousPage> Pages = new[]
    {
        new ContinuousPage(0, 1.5),
        new ContinuousPage(1, 1.0),
        new ContinuousPage(2, 2.0),
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 160)]
    [InlineData(2, 270)]
    public void PageTop_SumsPreviousPagesAndSpacing(int pageIndex, double expectedTop)
    {
        Assert.Equal(expectedTop, ContinuousLayout.PageTop(Pages, pageIndex, width: 100, spacing: 10));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(155, 0)]
    [InlineData(160, 1)]
    [InlineData(269, 1)]
    [InlineData(270, 2)]
    [InlineData(10_000, 2)]
    public void PageAt_FindsPageCoveringOffset(double y, int expectedPage)
    {
        Assert.Equal(expectedPage, ContinuousLayout.PageAt(Pages, y, width: 100, spacing: 10));
    }
}
