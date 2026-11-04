using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

/// <summary>
/// The sheet sort chosen by the user, shared by the library root and folders.
/// Defaults to name ascending.
/// </summary>
public interface ISortPreferenceService
{
    SortField SheetSortField { get; }

    SortDirection SheetSortDirection { get; }

    void Save(SortField field, SortDirection direction);
}
