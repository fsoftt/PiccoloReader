using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;
    private readonly ISortPreferenceService? _sortPreference;
    private readonly IConcertQueue? _concertQueue;

    private List<Folder> _allFolders = new();
    private List<Sheet> _allRootSheets = new();

    public LibraryViewModel(LibraryService libraryService, PdfImportService importService, ISortPreferenceService? sortPreference = null, IConcertQueue? concertQueue = null)
    {
        _libraryService = libraryService;
        _importService = importService;
        _sortPreference = sortPreference;
        _concertQueue = concertQueue;
        LoadSavedSort();
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

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasNoSheetResults;

    [ObservableProperty]
    private bool _hasSheetResults;

    [ObservableProperty]
    private bool _canReorderSheets;

    partial void OnFolderSearchTextChanged(string value) => RefreshFolders();

    partial void OnSheetSearchTextChanged(string value) => RefreshSheets();

    public async Task LoadAsync()
    {
        LoadSavedSort();

        _allFolders = (await _libraryService.GetFoldersAsync()).ToList();
        var counts = await _libraryService.GetSheetCountsByFolderAsync();
        foreach (var folder in _allFolders)
        {
            folder.SheetCount = counts.GetValueOrDefault(folder.Id);
        }

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

        var totalSheets = _allRootSheets.Count + counts.Values.Sum();
        SummaryText = AppStrings.LibrarySummary(totalSheets, _allFolders.Count);
        IsEmpty = _allFolders.Count == 0 && _allRootSheets.Count == 0;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var name = FolderNameNormalizer.Normalize(NewFolderName);
        if (name is null)
        {
            return;
        }

        await _libraryService.CreateFolderAsync(name);
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
    private async Task DeleteSheetAsync((Sheet Sheet, bool DeleteFile) args)
    {
        await _libraryService.DeleteSheetAsync(args.Sheet, args.DeleteFile);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderAsync((Folder Folder, bool DeleteFiles) args)
    {
        await _libraryService.DeleteFolderAsync(args.Folder.Id, deleteSheets: true, deleteExternalCopies: args.DeleteFiles);
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
        _sortPreference?.Save(field, direction);
        RefreshSheets();
    }

    private void LoadSavedSort()
    {
        if (_sortPreference is null)
        {
            return;
        }

        SheetSortField = _sortPreference.SheetSortField;
        SheetSortDirection = _sortPreference.SheetSortDirection;
    }

    // Called when a drag-and-drop ends: the visible order becomes the custom order.
    public async Task CommitSheetOrderAsync()
    {
        if (!CanReorderSheets)
        {
            return;
        }

        var ordered = RootSheets.ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        SheetSortField = SortField.Custom;
        SheetSortDirection = SortDirection.Ascending;
        _sortPreference?.Save(SortField.Custom, SortDirection.Ascending);

        await _libraryService.SetSheetOrderAsync(null, ordered.Select(s => s.Id).ToList());
    }

    // The whole root list in the current sort, ignoring the search filter.
    public void PublishConcertQueue() =>
        _concertQueue?.SetQueue(SheetSorter.Sort(_allRootSheets, SheetSortField, SheetSortDirection).Select(s => s.Id));

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

        RootSheets.SyncWith(SheetSorter.Sort(filtered, SheetSortField, SheetSortDirection));
        CanReorderSheets = string.IsNullOrWhiteSpace(SheetSearchText) && RootSheets.Count > 1;

        HasSheetResults = RootSheets.Count > 0;
        HasNoSheetResults = _allRootSheets.Count > 0 && RootSheets.Count == 0;
    }

    [RelayCommand]
    private async Task MoveSheetAsync((Sheet Sheet, int? TargetFolderId) args)
    {
        await _libraryService.MoveSheetAsync(args.Sheet, args.TargetFolderId);
        await LoadAsync();
    }
}
