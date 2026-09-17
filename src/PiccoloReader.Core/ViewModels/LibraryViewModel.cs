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
    private SortField _folderSortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _folderSortDirection = SortDirection.Ascending;

    [ObservableProperty]
    private SortField _sheetSortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _sheetSortDirection = SortDirection.Ascending;

    public async Task LoadAsync()
    {
        Folders.Clear();
        foreach (var folder in await _libraryService.GetFoldersAsync())
        {
            Folders.Add(folder);
        }

        RootSheets.Clear();
        foreach (var sheet in await _libraryService.GetSheetsAsync(null))
        {
            RootSheets.Add(sheet);
        }
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

        var sorted = field switch
        {
            SortField.DateAdded => direction == SortDirection.Ascending
                ? Folders.OrderBy(f => f.DateAdded).ToList()
                : Folders.OrderByDescending(f => f.DateAdded).ToList(),
            _ => direction == SortDirection.Ascending
                ? Folders.OrderBy(f => f.Name).ToList()
                : Folders.OrderByDescending(f => f.Name).ToList()
        };

        Folders.Clear();
        foreach (var folder in sorted)
        {
            Folders.Add(folder);
        }
    }

    public void ApplySheetSort(SortField field, SortDirection direction)
    {
        SheetSortField = field;
        SheetSortDirection = direction;

        var sorted = field switch
        {
            SortField.DateAdded => direction == SortDirection.Ascending
                ? RootSheets.OrderBy(s => s.DateAdded).ToList()
                : RootSheets.OrderByDescending(s => s.DateAdded).ToList(),
            _ => direction == SortDirection.Ascending
                ? RootSheets.OrderBy(s => s.Title).ToList()
                : RootSheets.OrderByDescending(s => s.Title).ToList()
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
