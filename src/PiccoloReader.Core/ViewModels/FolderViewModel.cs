using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.ViewModels;

public partial class FolderViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;
    private readonly ISortPreferenceService? _sortPreference;
    private readonly IConcertQueue? _concertQueue;

    private List<Sheet> _allSheets = new();

    public FolderViewModel(LibraryService libraryService, PdfImportService importService, ISortPreferenceService? sortPreference = null, IConcertQueue? concertQueue = null)
    {
        _libraryService = libraryService;
        _importService = importService;
        _sortPreference = sortPreference;
        _concertQueue = concertQueue;
        LoadSavedSort();
    }

    [ObservableProperty]
    private int _folderId;

    [ObservableProperty]
    private bool _hasMultipleSheets;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private SortField _sortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isFolderEmpty;

    [ObservableProperty]
    private bool _hasNoSearchResults;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _canReorder;

    public ObservableCollection<Sheet> Sheets { get; } = new();

    partial void OnSearchTextChanged(string value) => RefreshSheets();

    // Returns false when the folder no longer exists (it was deleted); the
    // list is then emptied and the view should leave the folder.
    public async Task<bool> LoadAsync()
    {
        LoadSavedSort();

        if (!(await _libraryService.GetFoldersAsync()).Any(f => f.Id == FolderId))
        {
            _allSheets = new List<Sheet>();
            HasMultipleSheets = false;
            RefreshSheets();
            return false;
        }

        _allSheets = (await _libraryService.GetSheetsAsync(FolderId)).ToList();
        HasMultipleSheets = _allSheets.Count > 1;
        SummaryText = AppStrings.SheetCount(_allSheets.Count);

        if (!HasMultipleSheets)
        {
            IsSearchVisible = false;
            SearchText = string.Empty;
        }

        RefreshSheets();
        return true;
    }

    [RelayCommand]
    private async Task ImportPdfAsync(string sourceFilePath)
    {
        await _importService.ImportAsync(sourceFilePath, FolderId);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSheetAsync(Sheet sheet)
    {
        await _libraryService.DeleteSheetAsync(sheet);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MoveSheetAsync((Sheet Sheet, int? TargetFolderId) args)
    {
        await _libraryService.MoveSheetAsync(args.Sheet, args.TargetFolderId);
        await LoadAsync();
    }

    public void ApplySort(SortField field, SortDirection direction)
    {
        SortField = field;
        SortDirection = direction;
        _sortPreference?.Save(field, direction);
        RefreshSheets();
    }

    private void LoadSavedSort()
    {
        if (_sortPreference is null)
        {
            return;
        }

        SortField = _sortPreference.SheetSortField;
        SortDirection = _sortPreference.SheetSortDirection;
    }

    // Called when a drag-and-drop ends: the visible order becomes the custom order.
    public async Task CommitSheetOrderAsync()
    {
        if (!CanReorder)
        {
            return;
        }

        var ordered = Sheets.ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        SortField = SortField.Custom;
        SortDirection = SortDirection.Ascending;
        _sortPreference?.Save(SortField.Custom, SortDirection.Ascending);

        await _libraryService.SetSheetOrderAsync(FolderId, ordered.Select(s => s.Id).ToList());
    }

    // The whole folder in the current sort, ignoring the search filter.
    public void PublishConcertQueue() =>
        _concertQueue?.SetQueue(SheetSorter.Sort(_allSheets, SortField, SortDirection).Select(s => s.Id));

    private void RefreshSheets()
    {
        IEnumerable<Sheet> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSheets
            : _allSheets.Where(s => s.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        Sheets.SyncWith(SheetSorter.Sort(filtered, SortField, SortDirection));
        CanReorder = string.IsNullOrWhiteSpace(SearchText) && Sheets.Count > 1;

        HasResults = Sheets.Count > 0;
        IsFolderEmpty = _allSheets.Count == 0;
        HasNoSearchResults = _allSheets.Count > 0 && Sheets.Count == 0;
    }
}
