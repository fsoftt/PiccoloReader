using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.ViewModels;

public static class SheetSorter
{
    public static List<Sheet> Sort(IEnumerable<Sheet> sheets, SortField field, SortDirection direction)
    {
        var ascending = direction == SortDirection.Ascending;
        return field switch
        {
            SortField.DateAdded => (ascending
                ? sheets.OrderBy(s => s.DateAdded)
                : sheets.OrderByDescending(s => s.DateAdded)).ToList(),
            SortField.Custom => (ascending
                ? sheets.OrderBy(s => s.SortOrder).ThenBy(s => s.Title)
                : sheets.OrderByDescending(s => s.SortOrder).ThenByDescending(s => s.Title)).ToList(),
            _ => (ascending
                ? sheets.OrderBy(s => s.Title)
                : sheets.OrderByDescending(s => s.Title)).ToList()
        };
    }
}
