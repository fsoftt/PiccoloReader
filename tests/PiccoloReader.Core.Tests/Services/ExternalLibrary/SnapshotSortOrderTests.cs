using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Tests.Services.ExternalLibrary;

public class SnapshotSortOrderTests
{
    [Fact]
    public void TryParse_SnapshotWithoutSortOrder_DefaultsToZero()
    {
        const string json = """
            { "Version": 1, "Sheets": [ { "SheetKey": "abc.pdf", "Title": "Old", "PageCount": 2 } ] }
            """;

        var snapshot = LibrarySnapshot.TryParse(json);

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot!.Sheets.Single().SortOrder);
    }

    [Fact]
    public void SortOrder_RoundTripsThroughJson()
    {
        var original = new LibrarySnapshot
        {
            Sheets = { new SnapshotSheet { SheetKey = "abc.pdf", Title = "New", SortOrder = 4 } }
        };

        var parsed = LibrarySnapshot.TryParse(original.ToJson())!;

        Assert.Equal(4, parsed.Sheets.Single().SortOrder);
    }
}
