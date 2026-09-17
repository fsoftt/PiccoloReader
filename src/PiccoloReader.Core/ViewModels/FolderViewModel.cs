using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class FolderViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;

    private List<Sheet> _allSheets = new();

    public FolderViewModel(LibraryService libraryService, PdfImportService importService)
    {
        _libraryService = libraryService;
        _importService = importService;
    }

    [ObservableProperty]
    private int _folderId;

    [ObservableProperty]
    private bool _hasSheets;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private SortField _sortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    public ObservableCollection<Sheet> Sheets { get; } = new();

    partial void OnSearchTextChanged(string value) => RefreshSheets();

    public async Task LoadAsync()
    {
        _allSheets = (await _libraryService.GetSheetsAsync(FolderId)).ToList();
        HasSheets = _allSheets.Count > 0;
        RefreshSheets();
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
    }
}
