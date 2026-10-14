using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class SharedPdfImportServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly SharedPdfImportService _sut;
    private readonly AppDatabase _database;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"shared-{Guid.NewGuid():N}");

    public SharedPdfImportServiceTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new SharedPdfImportService(new PdfImportService(_database, _storage));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ImportAsync_ValidPdf_ImportsIntoFolderAndDeletesTempFile()
    {
        var path = WriteFile("Sonata.pdf", "%PDF-1.7\nbody");

        var result = await _sut.ImportAsync(new[] { path }, folderId: 3);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal("Sonata", result.FirstImportedTitle);
        Assert.False(File.Exists(path));
        var sheets = await _database.Connection.Table<PiccoloReader.Core.Data.Models.Sheet>().ToListAsync();
        Assert.Equal(3, Assert.Single(sheets).FolderId);
    }

    [Fact]
    public async Task ImportAsync_NonPdf_CountsAsFailedAndImportsNothing()
    {
        var path = WriteFile("notes.pdf", "just text");

        var result = await _sut.ImportAsync(new[] { path }, folderId: null);

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(await _database.Connection.Table<PiccoloReader.Core.Data.Models.Sheet>().ToListAsync());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ImportAsync_MissingFile_DoesNotBlockOthers()
    {
        var good = WriteFile("Good.pdf", "%PDF-1.4");
        var missing = Path.Combine(_tempDir, "missing.pdf");

        var result = await _sut.ImportAsync(new[] { missing, good }, folderId: null);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public void Discard_DeletesFiles()
    {
        var path = WriteFile("x.pdf", "%PDF-");

        SharedPdfImportService.Discard(new[] { path });

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("Moonlight Sonata.pdf", "Moonlight Sonata.pdf")]
    [InlineData("Moonlight Sonata", "Moonlight Sonata.pdf")]
    [InlineData("a/b\\c.pdf", "c.pdf")]
    [InlineData("  ", "PDF.pdf")]
    [InlineData(null, "PDF.pdf")]
    [InlineData(".pdf", "PDF.pdf")]
    public void SanitizeFileName_ProducesSafePdfName(string? input, string expected)
    {
        Assert.Equal(expected, SharedPdfImportService.SanitizeFileName(input, "PDF"));
    }
}
