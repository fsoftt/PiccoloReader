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

    public LibraryViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        var libraryService = new LibraryService(database, _storage);
        var importService = new PdfImportService(database, _storage);
        _sut = new LibraryViewModel(libraryService, importService);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task CreateFolderCommand_AddsFolderToFoldersCollection()
    {
        _sut.NewFolderName = "Big Band";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Single(_sut.Folders);
        Assert.Equal("Big Band", _sut.Folders[0].Name);
        Assert.Equal(string.Empty, _sut.NewFolderName);
    }

    [Fact]
    public async Task CreateFolderCommand_BlankName_DoesNotCreateFolder()
    {
        _sut.NewFolderName = "   ";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Empty(_sut.Folders);
    }

    [Fact]
    public async Task ImportPdfCommand_AddsSheetToRootSheets()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");

        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);

        Assert.Single(_sut.RootSheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public async Task DeleteFolderCommand_RemovesFolderFromCollection()
    {
        _sut.NewFolderName = "Tropical";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        await _sut.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(_sut.Folders);
    }

    [Fact]
    public void ApplySheetSort_NameDescending_OrdersRootSheetsReverseAlphabetically()
    {
        _sut.RootSheets.Add(new Sheet { Title = "A-Piece", DateAdded = DateTime.UtcNow });
        _sut.RootSheets.Add(new Sheet { Title = "Z-Piece", DateAdded = DateTime.UtcNow });

        _sut.ApplySheetSort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.RootSheets[0].Title);
        Assert.Equal("A-Piece", _sut.RootSheets[1].Title);
    }

    [Fact]
    public void ApplySheetSort_DateAddedDescending_OrdersRootSheetsNewestFirst()
    {
        _sut.RootSheets.Add(new Sheet { Title = "Older", DateAdded = DateTime.UtcNow.AddDays(-1) });
        _sut.RootSheets.Add(new Sheet { Title = "Newer", DateAdded = DateTime.UtcNow });

        _sut.ApplySheetSort(SortField.DateAdded, SortDirection.Descending);

        Assert.Equal("Newer", _sut.RootSheets[0].Title);
        Assert.Equal("Older", _sut.RootSheets[1].Title);
    }

    [Fact]
    public void ApplyFolderSort_NameDescending_OrdersFoldersReverseAlphabetically()
    {
        _sut.Folders.Add(new Folder { Name = "A-Folder", DateAdded = DateTime.UtcNow });
        _sut.Folders.Add(new Folder { Name = "Z-Folder", DateAdded = DateTime.UtcNow });

        _sut.ApplyFolderSort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Folder", _sut.Folders[0].Name);
        Assert.Equal("A-Folder", _sut.Folders[1].Name);
    }

    [Fact]
    public void ApplyFolderSort_DateAddedDescending_OrdersFoldersNewestFirst()
    {
        _sut.Folders.Add(new Folder { Name = "Older", DateAdded = DateTime.UtcNow.AddDays(-1) });
        _sut.Folders.Add(new Folder { Name = "Newer", DateAdded = DateTime.UtcNow });

        _sut.ApplyFolderSort(SortField.DateAdded, SortDirection.Descending);

        Assert.Equal("Newer", _sut.Folders[0].Name);
        Assert.Equal("Older", _sut.Folders[1].Name);
    }

    [Fact]
    public async Task MoveSheetCommand_MovesRootSheetIntoFolder()
    {
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);
        var sheet = _sut.RootSheets[0];

        await _sut.MoveSheetCommand.ExecuteAsync((sheet, (int?)folder.Id));

        Assert.Empty(_sut.RootSheets);
        File.Delete(sourcePath);
    }
}
