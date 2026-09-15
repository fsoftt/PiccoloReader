using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class FolderViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _libraryService;
    private readonly FolderViewModel _sut;

    public FolderViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(database, _storage);
        var importService = new PdfImportService(database, _storage);
        _sut = new FolderViewModel(_libraryService, importService);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task LoadAsync_PopulatesSheetsForFolderId()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);

        await _sut.LoadAsync();

        Assert.Single(_sut.Sheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public async Task MoveSheetCommand_MovesSheetOutOfCurrentFolder()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);
        var sheet = _sut.Sheets[0];

        await _sut.MoveSheetCommand.ExecuteAsync((sheet, (int?)null));

        Assert.Empty(_sut.Sheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public void ApplySort_NameDescending_OrdersSheetsReverseAlphabetically()
    {
        _sut.Sheets.Add(new Sheet { Title = "A-Piece", DateAdded = DateTime.UtcNow });
        _sut.Sheets.Add(new Sheet { Title = "Z-Piece", DateAdded = DateTime.UtcNow });

        _sut.ApplySort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.Sheets[0].Title);
        Assert.Equal("A-Piece", _sut.Sheets[1].Title);
    }
}
