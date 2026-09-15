using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class PdfImportServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly PdfImportService _sut;
    private readonly string _sourceFilePath;

    public PdfImportServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new PdfImportService(database, _storage);

        _sourceFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-My Piece.pdf");
        File.WriteAllText(_sourceFilePath, "fake-pdf-content");
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (File.Exists(_sourceFilePath))
        {
            File.Delete(_sourceFilePath);
        }
    }

    [Fact]
    public async Task ImportAsync_CopiesFileIntoSheetsDirectory()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: null);

        var copiedPath = Path.Combine(_storage.SheetsDirectory, sheet.FileName);
        Assert.True(File.Exists(copiedPath));
        Assert.NotEqual(_sourceFilePath, copiedPath);
    }

    [Fact]
    public async Task ImportAsync_UsesSourceFileNameAsTitle()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: null);

        Assert.EndsWith("My Piece", sheet.Title);
    }

    [Fact]
    public async Task ImportAsync_SetsFolderIdAndDefaultPageCount()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: 7);

        Assert.Equal(7, sheet.FolderId);
        Assert.Equal(0, sheet.PageCount);
    }
}
