using PiccoloReader.Core.Data;
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
}
