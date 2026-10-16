using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class SheetShareServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _annotations;
    private readonly FakeExporter _exporter = new();
    private readonly string _shareDir = Path.Combine(Path.GetTempPath(), $"share-{Guid.NewGuid():N}");
    private readonly SheetShareService _sut;
    private readonly Sheet _sheet = new() { Id = 7, Title = "My Song.pdf", FileName = "abc.pdf" };

    public SheetShareServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _annotations = new AnnotationService(database);
        Directory.CreateDirectory(_storage.SheetsDirectory);
        File.WriteAllText(Path.Combine(_storage.SheetsDirectory, "abc.pdf"), "ORIGINAL");
        _sut = new SheetShareService(_storage, _annotations, _exporter, _shareDir);
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_shareDir))
        {
            Directory.Delete(_shareDir, true);
        }
    }

    [Theory]
    [InlineData("My Song", "My Song.pdf")]
    [InlineData("My Song.PDF", "My Song.pdf")]
    [InlineData("a/b:c*d?.pdf", "a_b_c_d_.pdf")]
    [InlineData("", "sheet.pdf")]
    [InlineData("   ", "sheet.pdf")]
    [InlineData("???", "sheet.pdf")]
    [InlineData("Ode...", "Ode.pdf")]
    public void BuildFileName_SanitizesTitle(string title, string expected) =>
        Assert.Equal(expected, SheetShareService.BuildFileName(title));

    [Fact]
    public void BuildFileName_NullTitle_UsesFallback() =>
        Assert.Equal("sheet.pdf", SheetShareService.BuildFileName(null));

    [Fact]
    public void BuildFileName_LongTitle_IsTruncated() =>
        Assert.Equal(104, SheetShareService.BuildFileName(new string('a', 300)).Length);

    [Fact]
    public async Task PrepareAsync_NoAnnotations_CopiesOriginalWithoutExporting()
    {
        var path = await _sut.PrepareAsync(_sheet, (0, 0));

        Assert.Equal(Path.Combine(_shareDir, "My Song.pdf"), path);
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
        Assert.Equal(0, _exporter.Calls);
    }

    [Fact]
    public async Task PrepareAsync_WithAnnotations_UsesExporter()
    {
        await _annotations.AddIconAsync(_sheet.Id, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

        var path = await _sut.PrepareAsync(_sheet, (0, 0));

        Assert.Equal(1, _exporter.Calls);
        Assert.Equal("EXPORTED", File.ReadAllText(path));
    }

    [Fact]
    public async Task PrepareAsync_AnnotationsOnOtherSheet_CopiesOriginal()
    {
        await _annotations.AddIconAsync(99, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

        var path = await _sut.PrepareAsync(_sheet, (0, 0));

        Assert.Equal("ORIGINAL", File.ReadAllText(path));
    }

    [Fact]
    public async Task PrepareAsync_RemovesPreviousShareFiles()
    {
        Directory.CreateDirectory(_shareDir);
        var old = Path.Combine(_shareDir, "old.pdf");
        File.WriteAllText(old, "x");

        await _sut.PrepareAsync(_sheet, (0, 0));

        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task PrepareAsync_ExporterFails_RethrowsAndLeavesNoPartialFile()
    {
        await _annotations.AddIconAsync(_sheet.Id, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        _exporter.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PrepareAsync(_sheet, (0, 0)));

        Assert.Empty(Directory.GetFiles(_shareDir));
    }

    [Fact]
    public async Task PrepareAsync_MissingSource_Throws()
    {
        var missing = new Sheet { Id = 1, Title = "x", FileName = "nope.pdf" };

        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.PrepareAsync(missing, (0, 0)));
    }

    [Fact]
    public async Task PrepareAsync_NoExporter_CopiesOriginalEvenWithAnnotations()
    {
        await _annotations.AddIconAsync(_sheet.Id, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        var sut = new SheetShareService(_storage, _annotations, null, _shareDir);

        var path = await sut.PrepareAsync(_sheet, (0, 0));

        Assert.False(sut.IsSupported);
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
    }

    private sealed class FakeExporter : IAnnotatedPdfExporter
    {
        public int Calls { get; private set; }

        public bool Fail { get; set; }

        public Task ExportAsync(string sourcePdfPath, int sheetId, string destinationPath, (double Width, double Height) legacyReferenceArea, CancellationToken cancellationToken = default)
        {
            Calls++;
            File.WriteAllText(destinationPath, "EXPORTED");
            if (Fail)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }
    }
}
