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
        _sut = new FolderViewModel(_libraryService, importService, new FakeAdsPreferenceService());
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
    public async Task ApplySort_NameDescending_OrdersSheetsReverseAlphabetically()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var aPath = Path.Combine(tempDir, "A-Piece.pdf");
        await File.WriteAllTextAsync(aPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(aPath);

        var zPath = Path.Combine(tempDir, "Z-Piece.pdf");
        await File.WriteAllTextAsync(zPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(zPath);

        _sut.ApplySort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.Sheets[0].Title);
        Assert.Equal("A-Piece", _sut.Sheets[1].Title);
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task SearchText_FiltersSheetsByTitleCaseInsensitive()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var noctPath = Path.Combine(tempDir, "Nocturne.pdf");
        await File.WriteAllTextAsync(noctPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(noctPath);

        var preludePath = Path.Combine(tempDir, "Prelude.pdf");
        await File.WriteAllTextAsync(preludePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(preludePath);

        _sut.SearchText = "noct";

        Assert.Single(_sut.Sheets);
        Assert.Equal("Nocturne", _sut.Sheets[0].Title);
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task SearchText_NoMatchesWithTwoSheets_KeepsHasMultipleSheetsTrue()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var noctPath = Path.Combine(tempDir, "Nocturne.pdf");
        await File.WriteAllTextAsync(noctPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(noctPath);

        var preludePath = Path.Combine(tempDir, "Prelude.pdf");
        await File.WriteAllTextAsync(preludePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(preludePath);

        _sut.SearchText = "nonexistent";

        Assert.Empty(_sut.Sheets);
        Assert.True(_sut.HasMultipleSheets);
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ImportPdfCommand_OneSheet_HasMultipleSheetsFalse()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);

        Assert.Single(_sut.Sheets);
        Assert.False(_sut.HasMultipleSheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public async Task ImportPdfCommand_TwoSheets_HasMultipleSheetsTrue()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var aPath = Path.Combine(tempDir, "A-Piece.pdf");
        await File.WriteAllTextAsync(aPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(aPath);

        var zPath = Path.Combine(tempDir, "Z-Piece.pdf");
        await File.WriteAllTextAsync(zPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(zPath);

        Assert.True(_sut.HasMultipleSheets);
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task DeleteSheetCommand_DownToOneSheet_ClosesSearch()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var noctPath = Path.Combine(tempDir, "Nocturne.pdf");
        await File.WriteAllTextAsync(noctPath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(noctPath);

        var preludePath = Path.Combine(tempDir, "Prelude.pdf");
        await File.WriteAllTextAsync(preludePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(preludePath);

        _sut.IsSearchVisible = true;
        _sut.SearchText = "noct";
        var toDelete = _sut.Sheets[0];

        await _sut.DeleteSheetCommand.ExecuteAsync(toDelete);

        Assert.False(_sut.HasMultipleSheets);
        Assert.False(_sut.IsSearchVisible);
        Assert.Equal(string.Empty, _sut.SearchText);
        Assert.Single(_sut.Sheets);
        Directory.Delete(tempDir, recursive: true);
    }
}
