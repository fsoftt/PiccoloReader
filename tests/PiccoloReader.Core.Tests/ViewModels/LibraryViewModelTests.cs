using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class LibraryViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryViewModel _sut;
    private readonly string _tempDir;

    public LibraryViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        var libraryService = new LibraryService(database, _storage);
        var importService = new PdfImportService(database, _storage);
        _sut = new LibraryViewModel(libraryService, importService);

        _tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _storage.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private async Task ImportSheetAsync(string title)
    {
        var sourcePath = Path.Combine(_tempDir, $"{title}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);
    }

    [Fact]
    public async Task CreateFolderCommand_AddsFolderToFoldersCollection()
    {
        _sut.NewFolderName = "Big Band";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Single(_sut.Folders);
        Assert.Equal("Big Band", _sut.Folders[0].Name);
        Assert.Equal(string.Empty, _sut.NewFolderName);
        Assert.True(_sut.HasFolders);
        Assert.False(_sut.HasMultipleFolders);
    }

    [Fact]
    public async Task CreateFolderCommand_TwoFolders_HasMultipleFoldersTrue()
    {
        _sut.NewFolderName = "Big Band";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.True(_sut.HasMultipleFolders);
    }

    [Fact]
    public async Task CreateFolderCommand_BlankName_DoesNotCreateFolder()
    {
        _sut.NewFolderName = "   ";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Empty(_sut.Folders);
        Assert.False(_sut.HasFolders);
    }

    [Fact]
    public async Task ImportPdfCommand_AddsSheetToRootSheets()
    {
        await ImportSheetAsync("test-sheet");

        Assert.Single(_sut.RootSheets);
        Assert.True(_sut.HasRootSheets);
        Assert.False(_sut.HasMultipleRootSheets);
    }

    [Fact]
    public async Task ImportPdfCommand_TwoSheets_HasMultipleRootSheetsTrue()
    {
        await ImportSheetAsync("A-Piece");
        await ImportSheetAsync("Z-Piece");

        Assert.True(_sut.HasMultipleRootSheets);
    }

    [Fact]
    public async Task DeleteFolderCommand_RemovesFolderFromCollection()
    {
        _sut.NewFolderName = "Tropical";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        await _sut.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(_sut.Folders);
        Assert.False(_sut.HasFolders);
    }

    [Fact]
    public async Task ApplySheetSort_NameDescending_OrdersRootSheetsReverseAlphabetically()
    {
        await ImportSheetAsync("A-Piece");
        await ImportSheetAsync("Z-Piece");

        _sut.ApplySheetSort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.RootSheets[0].Title);
        Assert.Equal("A-Piece", _sut.RootSheets[1].Title);
    }

    [Fact]
    public async Task ApplySheetSort_DateAddedDescending_OrdersRootSheetsNewestFirst()
    {
        await ImportSheetAsync("Older");
        await Task.Delay(10);
        await ImportSheetAsync("Newer");

        _sut.ApplySheetSort(SortField.DateAdded, SortDirection.Descending);

        Assert.Equal("Newer", _sut.RootSheets[0].Title);
        Assert.Equal("Older", _sut.RootSheets[1].Title);
    }

    [Fact]
    public async Task ApplyFolderSort_NameDescending_OrdersFoldersReverseAlphabetically()
    {
        _sut.NewFolderName = "A-Folder";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.NewFolderName = "Z-Folder";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        _sut.ApplyFolderSort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Folder", _sut.Folders[0].Name);
        Assert.Equal("A-Folder", _sut.Folders[1].Name);
    }

    [Fact]
    public async Task ApplyFolderSort_DateAddedDescending_OrdersFoldersNewestFirst()
    {
        _sut.NewFolderName = "Older";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        await Task.Delay(10);
        _sut.NewFolderName = "Newer";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        _sut.ApplyFolderSort(SortField.DateAdded, SortDirection.Descending);

        Assert.Equal("Newer", _sut.Folders[0].Name);
        Assert.Equal("Older", _sut.Folders[1].Name);
    }

    [Fact]
    public async Task FolderSearchText_FiltersFoldersByNameCaseInsensitive()
    {
        _sut.NewFolderName = "Big Band";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        _sut.FolderSearchText = "big";

        Assert.Single(_sut.Folders);
        Assert.Equal("Big Band", _sut.Folders[0].Name);
    }

    [Fact]
    public async Task DeleteFolderCommand_DownToOneFolder_ClosesFolderSearch()
    {
        _sut.NewFolderName = "Big Band";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.IsFolderSearchVisible = true;
        _sut.FolderSearchText = "big";
        var toDelete = _sut.Folders[0];

        await _sut.DeleteFolderCommand.ExecuteAsync(toDelete);

        Assert.False(_sut.HasMultipleFolders);
        Assert.False(_sut.IsFolderSearchVisible);
        Assert.Equal(string.Empty, _sut.FolderSearchText);
        Assert.Single(_sut.Folders);
    }

    [Fact]
    public async Task FolderSearchText_NoMatches_KeepsHasFoldersTrue()
    {
        _sut.NewFolderName = "Big Band";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        _sut.FolderSearchText = "nonexistent";

        Assert.Empty(_sut.Folders);
        Assert.True(_sut.HasFolders);
    }

    [Fact]
    public async Task FolderSearchText_NoMatchesWithTwoFolders_KeepsHasMultipleFoldersTrue()
    {
        _sut.NewFolderName = "Big Band";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);

        _sut.FolderSearchText = "nonexistent";

        Assert.Empty(_sut.Folders);
        Assert.True(_sut.HasMultipleFolders);
    }

    [Fact]
    public async Task SheetSearchText_FiltersRootSheetsByTitleCaseInsensitive()
    {
        await ImportSheetAsync("Nocturne");
        await ImportSheetAsync("Prelude");

        _sut.SheetSearchText = "noct";

        Assert.Single(_sut.RootSheets);
        Assert.Equal("Nocturne", _sut.RootSheets[0].Title);
    }

    [Fact]
    public async Task SheetSearchText_NoMatches_KeepsHasRootSheetsTrue()
    {
        await ImportSheetAsync("Nocturne");

        _sut.SheetSearchText = "nonexistent";

        Assert.Empty(_sut.RootSheets);
        Assert.True(_sut.HasRootSheets);
    }

    [Fact]
    public async Task SheetSearchText_NoMatchesWithTwoSheets_KeepsHasMultipleRootSheetsTrue()
    {
        await ImportSheetAsync("Nocturne");
        await ImportSheetAsync("Prelude");

        _sut.SheetSearchText = "nonexistent";

        Assert.Empty(_sut.RootSheets);
        Assert.True(_sut.HasMultipleRootSheets);
    }

    [Fact]
    public async Task DeleteSheetCommand_DownToOneSheet_ClosesSheetSearch()
    {
        await ImportSheetAsync("Nocturne");
        await ImportSheetAsync("Prelude");
        _sut.IsSheetSearchVisible = true;
        _sut.SheetSearchText = "noct";
        var toDelete = _sut.RootSheets[0];

        await _sut.DeleteSheetCommand.ExecuteAsync(toDelete);

        Assert.False(_sut.HasMultipleRootSheets);
        Assert.False(_sut.IsSheetSearchVisible);
        Assert.Equal(string.Empty, _sut.SheetSearchText);
        Assert.Single(_sut.RootSheets);
    }

    [Fact]
    public async Task MoveSheetCommand_MovesRootSheetIntoFolder()
    {
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        await ImportSheetAsync("test-sheet");
        var sheet = _sut.RootSheets[0];

        await _sut.MoveSheetCommand.ExecuteAsync((sheet, (int?)folder.Id));

        Assert.Empty(_sut.RootSheets);
    }
}
