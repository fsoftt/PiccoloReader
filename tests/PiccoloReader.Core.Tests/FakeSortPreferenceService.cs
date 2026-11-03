using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class FakeSortPreferenceService : ISortPreferenceService
{
    public SortField SheetSortField { get; set; } = SortField.Name;

    public SortDirection SheetSortDirection { get; set; } = SortDirection.Ascending;

    public void Save(SortField field, SortDirection direction)
    {
        SheetSortField = field;
        SheetSortDirection = direction;
    }
}
