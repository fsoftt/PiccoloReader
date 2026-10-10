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

    [Theory]
    [InlineData(1, "1 annotation")]
    [InlineData(2, "2 annotations")]
    public void AnnotationCount_UsesSingularOnlyForOne(int count, string expected) =>
        Assert.Equal(expected, AppStrings.AnnotationCount(count));

    [Theory]
    [InlineData(1, "1 bookmark")]
    [InlineData(0, "0 bookmarks")]
    public void BookmarkCount_UsesSingularOnlyForOne(int count, string expected) =>
        Assert.Equal(expected, AppStrings.BookmarkCount(count));

    [Theory]
    [InlineData(1, "1 crop")]
    [InlineData(3, "3 crops")]
    public void CropCount_UsesSingularOnlyForOne(int count, string expected) =>
        Assert.Equal(expected, AppStrings.CropCount(count));

    [Fact]
    public void RecoverResult_ListsEveryKindWithSingularAndPlural() =>
        Assert.Equal(
            "Restored 2 sheets, 1 folder, 2 annotations, 1 bookmark and 1 crop.",
            AppStrings.RecoverResult(2, 1, 2, 1, 1));

    [Fact]
    public void RecoverResult_OmitsKindsThatAreZero()
    {
        Assert.Equal("Restored 1 sheet.", AppStrings.RecoverResult(1, 0, 0, 0, 0));
        Assert.Equal("Restored 2 sheets and 3 crops.", AppStrings.RecoverResult(2, 0, 0, 0, 3));
    }
}
