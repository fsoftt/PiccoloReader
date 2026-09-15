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

    public FolderViewModel(LibraryService libraryService, PdfImportService importService)
    {
        _libraryService = libraryService;
        _importService = importService;
    }

    [ObservableProperty]
    private int _folderId;

    public ObservableCollection<Sheet> Sheets { get; } = new();

    public async Task LoadAsync()
    {
        Sheets.Clear();
        foreach (var sheet in await _libraryService.GetSheetsAsync(FolderId))
        {
            Sheets.Add(sheet);
        }
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
}
