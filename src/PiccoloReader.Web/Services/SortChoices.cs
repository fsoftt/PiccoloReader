using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Web.Services;

// The sort menu entries of the library and folder pages (labels from AppStrings).
public static class SortChoices
{
    private static readonly (SortField Field, SortDirection Dir)[] All =
    [
        (SortField.Name, SortDirection.Ascending), (SortField.Name, SortDirection.Descending),
        (SortField.DateAdded, SortDirection.Ascending), (SortField.DateAdded, SortDirection.Descending),
        (SortField.Custom, SortDirection.Ascending),
    ];

    public static IReadOnlyList<string> SheetLabels => All.Select(Label).ToList();

    public static IReadOnlyList<string> FolderLabels => All.Where(c => c.Field != SortField.Custom).Select(Label).ToList();

    public static (SortField, SortDirection) Parse(string label) => All.First(c => Label(c) == label);

    private static string Label((SortField Field, SortDirection Dir) c) => (c.Field, c.Dir) switch
    {
        (SortField.Name, SortDirection.Ascending) => AppStrings.SortNameAscending,
        (SortField.Name, _) => AppStrings.SortNameDescending,
        (SortField.DateAdded, SortDirection.Ascending) => AppStrings.SortDateAddedAscending,
        (SortField.DateAdded, _) => AppStrings.SortDateAddedDescending,
        _ => AppStrings.SortCustom,
    };
}
