using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Tests.Services;

public class AppStringsPluralTests
{
    [Theory]
    [InlineData(0, "0 sheets")]
    [InlineData(1, "1 sheet")]
    [InlineData(2, "2 sheets")]
    public void SheetCount_UsesSingularOnlyForOne(int count, string expected) =>
        Assert.Equal(expected, AppStrings.SheetCount(count));

    [Fact]
    public void LibrarySummary_CombinesSingularAndPluralCounts() =>
        Assert.Equal("1 sheet · 3 folders", AppStrings.LibrarySummary(1, 3));

    [Theory]
    [InlineData(1, "1 p.")]
    [InlineData(12, "12 pp.")]
    public void Pages_UsesSingularOnlyForOne(int count, string expected) =>
        Assert.Equal(expected, AppStrings.Pages(count));
}
