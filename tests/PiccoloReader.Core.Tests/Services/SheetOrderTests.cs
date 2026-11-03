using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class SheetOrderTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _library;
    private readonly PdfImportService _import;
    private readonly string _tempDir;

    public SheetOrderTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _library = new LibraryService(database, _storage);
        _import = new PdfImportService(database, _storage);
        _tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _storage.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<Sheet> ImportAsync(string title, int? folderId)
    {
        var path = Path.Combine(_tempDir, $"{title}.pdf");
        await File.WriteAllTextAsync(path, "fake-pdf");
        return await _import.ImportAsync(path, folderId);
    }

    [Fact]
    public async Task Import_AssignsIncreasingSortOrderPerFolder()
    {
        var folder = await _library.CreateFolderAsync("F");

        var a = await ImportAsync("a", null);
        var b = await ImportAsync("b", null);
        var x = await ImportAsync("x", folder.Id);
        var y = await ImportAsync("y", folder.Id);

        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);
        Assert.Equal(0, x.SortOrder);
        Assert.Equal(1, y.SortOrder);
    }

    [Fact]
    public async Task Import_PersistsSortOrder()
    {
        await ImportAsync("a", null);
        await ImportAsync("b", null);

        var stored = await _library.GetSheetsAsync(null);

        Assert.Equal(new[] { 0, 1 }, stored.Select(s => s.SortOrder));
    }

    [Fact]
    public async Task MoveSheet_AppendsAtEndOfTarget()
    {
        var folder = await _library.CreateFolderAsync("F");
        await ImportAsync("x", folder.Id);
        await ImportAsync("y", folder.Id);
        var moved = await ImportAsync("m", null);

        await _library.MoveSheetAsync(moved, folder.Id);

        var inFolder = await _library.GetSheetsAsync(folder.Id);
        Assert.Equal(2, moved.SortOrder);
        Assert.Equal(new[] { "x", "y", "m" }, inFolder.Select(s => s.Title));
    }

    [Fact]
    public async Task MoveSheet_ToRoot_AppendsAfterExistingRootSheets()
    {
        var folder = await _library.CreateFolderAsync("F");
        await ImportAsync("r", null);
        var moved = await ImportAsync("m", folder.Id);

        await _library.MoveSheetAsync(moved, null);

        Assert.Equal(1, (await _library.GetSheetAsync(moved.Id)).SortOrder);
    }

    [Fact]
    public async Task SetSheetOrderAsync_PersistsGivenOrder()
    {
        var a = await ImportAsync("a", null);
        var b = await ImportAsync("b", null);
        var c = await ImportAsync("c", null);

        await _library.SetSheetOrderAsync(null, new[] { c.Id, a.Id, b.Id });

        var stored = await _library.GetSheetsAsync(null);
        Assert.Equal(new[] { "c", "a", "b" }, stored.Select(s => s.Title));
        Assert.Equal(new[] { 0, 1, 2 }, stored.Select(s => s.SortOrder));
    }

    [Fact]
    public async Task SetSheetOrderAsync_IgnoresIdsOfOtherFolders()
    {
        var folder = await _library.CreateFolderAsync("F");
        var inFolder = await ImportAsync("x", folder.Id);
        await ImportAsync("y", folder.Id);
        var root1 = await ImportAsync("r1", null);
        var root2 = await ImportAsync("r2", null);

        await _library.SetSheetOrderAsync(null, new[] { root2.Id, root1.Id, inFolder.Id });

        Assert.Equal(new[] { "r2", "r1" }, (await _library.GetSheetsAsync(null)).Select(s => s.Title));
        Assert.Equal(0, (await _library.GetSheetAsync(inFolder.Id)).SortOrder);
    }

    [Fact]
    public void CustomSort_OrdersBySortOrderThenTitle()
    {
        var sheets = new List<Sheet>
        {
            new() { Id = 1, Title = "b", SortOrder = 1 },
            new() { Id = 2, Title = "z", SortOrder = 0 },
            new() { Id = 3, Title = "a", SortOrder = 1 },
        };

        var sorted = SheetSorter.Sort(sheets, SortField.Custom, SortDirection.Ascending);

        Assert.Equal(new[] { 2, 3, 1 }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void NameAndDateSort_StillWork()
    {
        var t = DateTime.UtcNow;
        var sheets = new List<Sheet>
        {
            new() { Id = 1, Title = "b", DateAdded = t },
            new() { Id = 2, Title = "a", DateAdded = t.AddDays(1) },
        };

        Assert.Equal(new[] { 2, 1 }, SheetSorter.Sort(sheets, SortField.Name, SortDirection.Ascending).Select(s => s.Id));
        Assert.Equal(new[] { 2, 1 }, SheetSorter.Sort(sheets, SortField.DateAdded, SortDirection.Descending).Select(s => s.Id));
    }
}
