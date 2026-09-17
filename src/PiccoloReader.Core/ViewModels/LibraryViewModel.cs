using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;

    private List<Folder> _allFolders = new();
    private List<Sheet> _allRootSheets = new();

    public LibraryViewModel(LibraryService libraryService, PdfImportService importService)
    {
        _libraryService = libraryService;
        _importService = importService;
    }

    public ObservableCollection<Folder> Folders { get; } = new();

    public ObservableCollection<Sheet> RootSheets { get; } = new();

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    private bool _hasFolders;

    [ObservableProperty]
    private bool _hasRootSheets;

    [ObservableProperty]
    private bool _hasMultipleFolders;

    [ObservableProperty]
    private bool _hasMultipleRootSheets;

    [ObservableProperty]
    private string _folderSearchText = string.Empty;

    [ObservableProperty]
    private string _sheetSearchText = string.Empty;

    [ObservableProperty]
    private bool _isFolderSearchVisible;

    [ObservableProperty]
    private bool _isSheetSearchVisible;

    [ObservableProperty]
    private SortField _folderSortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _folderSortDirection = SortDirection.Ascending;

    [ObservableProperty]
    private SortField _sheetSortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _sheetSortDirection = SortDirection.Ascending;

    partial void OnFolderSearchTextChanged(string value) => RefreshFolders();

    partial void OnSheetSearchTextChanged(string value) => RefreshSheets();

    public async Task LoadAsync()
    {
        _allFolders = (await _libraryService.GetFoldersAsync()).ToList();
        HasFolders = _allFolders.Count > 0;
        HasMultipleFolders = _allFolders.Count > 1;

        if (!HasMultipleFolders)
        {
            IsFolderSearchVisible = false;
            FolderSearchText = string.Empty;
        }

        RefreshFolders();

        _allRootSheets = (await _libraryService.GetSheetsAsync(null)).ToList();
        HasRootSheets = _allRootSheets.Count > 0;
        HasMultipleRootSheets = _allRootSheets.Count > 1;

        if (!HasMultipleRootSheets)
        {
            IsSheetSearchVisible = false;
            SheetSearchText = string.Empty;
        }

        RefreshSheets();
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
        {
            return;
        }

        await _libraryService.CreateFolderAsync(NewFolderName);
        NewFolderName = string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ImportPdfAsync(string sourceFilePath)
    {
        await _importService.ImportAsync(sourceFilePath, folderId: null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSheetAsync(Sheet sheet)
    {
        await _libraryService.DeleteSheetAsync(sheet);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(Folder folder)
    {
        await _libraryService.DeleteFolderAsync(folder.Id, deleteSheets: true);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderKeepSheetsAsync(Folder folder)
    {
        await _libraryService.DeleteFolderAsync(folder.Id, deleteSheets: false);
        await LoadAsync();
    }

    public void ApplyFolderSort(SortField field, SortDirection direction)
    {
        FolderSortField = field;
        FolderSortDirection = direction;
        RefreshFolders();
    }

    public void ApplySheetSort(SortField field, SortDirection direction)
    {
        SheetSortField = field;
        SheetSortDirection = direction;
        RefreshSheets();
    }

    private void RefreshFolders()
    {
        IEnumerable<Folder> filtered = string.IsNullOrWhiteSpace(FolderSearchText)
            ? _allFolders
            : _allFolders.Where(f => f.Name.Contains(FolderSearchText, StringComparison.OrdinalIgnoreCase));

        var sorted = FolderSortField switch
        {
            SortField.DateAdded => FolderSortDirection == SortDirection.Ascending
                ? filtered.OrderBy(f => f.DateAdded)
                : filtered.OrderByDescending(f => f.DateAdded),
            _ => FolderSortDirection == SortDirection.Ascending
                ? filtered.OrderBy(f => f.Name)
                : filtered.OrderByDescending(f => f.Name)
        };

        Folders.Clear();
        foreach (var folder in sorted)
        {
            Folders.Add(folder);
        }
    }

    private void RefreshSheets()
    {
        IEnumerable<Sheet> filtered = string.IsNullOrWhiteSpace(SheetSearchText)
            ? _allRootSheets
            : _allRootSheets.Where(s => s.Title.Contains(SheetSearchText, StringComparison.OrdinalIgnoreCase));

        var sorted = SheetSortField switch
        {
            SortField.DateAdded => SheetSortDirection == SortDirection.Ascending
                ? filtered.OrderBy(s => s.DateAdded)
                : filtered.OrderByDescending(s => s.DateAdded),
            _ => SheetSortDirection == SortDirection.Ascending
                ? filtered.OrderBy(s => s.Title)
                : filtered.OrderByDescending(s => s.Title)
        };

        RootSheets.Clear();
        foreach (var sheet in sorted)
        {
            RootSheets.Add(sheet);
        }
    }

    [RelayCommand]
    private async Task MoveSheetAsync((Sheet Sheet, int? TargetFolderId) args)
    {
        await _libraryService.MoveSheetAsync(args.Sheet, args.TargetFolderId);
        await LoadAsync();
    }
}
