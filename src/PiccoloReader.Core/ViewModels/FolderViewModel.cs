using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class FolderViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;
    private readonly IAdsPreferenceService _adsPreferenceService;

    private List<Sheet> _allSheets = new();

    public FolderViewModel(LibraryService libraryService, PdfImportService importService, IAdsPreferenceService adsPreferenceService)
    {
        _libraryService = libraryService;
        _importService = importService;
        _adsPreferenceService = adsPreferenceService;
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
    private bool _showAdsBanner;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isFolderEmpty;

    [ObservableProperty]
    private bool _hasNoSearchResults;

    [ObservableProperty]
    private bool _hasResults;

    public ObservableCollection<Sheet> Sheets { get; } = new();

    partial void OnSearchTextChanged(string value) => RefreshSheets();

    // Returns false when the folder no longer exists (it was deleted); the
    // list is then emptied and the view should leave the folder.
    public async Task<bool> LoadAsync()
    {
        ShowAdsBanner = _adsPreferenceService.GetSupportWithAdsEnabled();

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
        RefreshSheets();
    }

    private void RefreshSheets()
    {
        IEnumerable<Sheet> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSheets
            : _allSheets.Where(s => s.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var sorted = SortField switch
        {
            SortField.DateAdded => SortDirection == SortDirection.Ascending
                ? filtered.OrderBy(s => s.DateAdded)
                : filtered.OrderByDescending(s => s.DateAdded),
            _ => SortDirection == SortDirection.Ascending
                ? filtered.OrderBy(s => s.Title)
                : filtered.OrderByDescending(s => s.Title)
        };

        Sheets.Clear();
        foreach (var sheet in sorted)
        {
            Sheets.Add(sheet);
        }

        HasResults = Sheets.Count > 0;
        IsFolderEmpty = _allSheets.Count == 0;
        HasNoSearchResults = _allSheets.Count > 0 && Sheets.Count == 0;
    }
}
