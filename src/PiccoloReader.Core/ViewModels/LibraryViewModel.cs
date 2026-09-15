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
}
