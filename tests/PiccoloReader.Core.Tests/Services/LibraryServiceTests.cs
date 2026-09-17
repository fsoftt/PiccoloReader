using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class LibraryServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _sut;

    public LibraryServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new LibraryService(database, _storage);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task CreateFolderAsync_AddsFolderRetrievableByGetFoldersAsync()
    {
        await _sut.CreateFolderAsync("Orchestra");

        var folders = await _sut.GetFoldersAsync();

        Assert.Single(folders);
        Assert.Equal("Orchestra", folders[0].Name);
    }

    [Fact]
    public async Task CreateFolderAsync_SetsDateAdded()
    {
        var before = DateTime.UtcNow;

        var folder = await _sut.CreateFolderAsync("Orchestra");

        Assert.True(folder.DateAdded >= before);
        Assert.True(folder.DateAdded <= DateTime.UtcNow);
    }

    [Fact]
    public async Task GetSheetsAsync_NullFolderId_ReturnsOnlyRootSheets()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        await _sut.MoveSheetAsync(await InsertSheetAsync(folder.Id), folder.Id);
        var rootSheet = await InsertSheetAsync(null);

        var rootSheets = await _sut.GetSheetsAsync(null);

        Assert.Single(rootSheets);
        Assert.Equal(rootSheet.Id, rootSheets[0].Id);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithDeleteSheetsTrue_RemovesFolderAndItsSheets()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(folder.Id);

        await _sut.DeleteFolderAsync(folder.Id, deleteSheets: true);

        Assert.Empty(await _sut.GetFoldersAsync());
        Assert.Empty(await _sut.GetSheetsAsync(folder.Id));
        Assert.False(File.Exists(Path.Combine(_storage.SheetsDirectory, sheet.FileName)));
    }

    [Fact]
    public async Task DeleteFolderAsync_WithDeleteSheetsFalse_MovesSheetsToRoot()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(folder.Id);

        await _sut.DeleteFolderAsync(folder.Id, deleteSheets: false);

        var rootSheets = await _sut.GetSheetsAsync(null);
        Assert.Single(rootSheets);
        Assert.Equal(sheet.Id, rootSheets[0].Id);
    }

    [Fact]
    public async Task MoveSheetAsync_UpdatesFolderId()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(null);

        await _sut.MoveSheetAsync(sheet, folder.Id);

        var sheetsInFolder = await _sut.GetSheetsAsync(folder.Id);
        Assert.Single(sheetsInFolder);
    }

    [Fact]
    public async Task DeleteSheetAsync_RemovesRowAndFile()
    {
        var sheet = await InsertSheetAsync(null);
        var filePath = Path.Combine(_storage.SheetsDirectory, sheet.FileName);

        await _sut.DeleteSheetAsync(sheet);

        Assert.Empty(await _sut.GetSheetsAsync(null));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task GetSheetAsync_ReturnsMatchingSheet()
    {
        var sheet = await InsertSheetAsync(null);

        var result = await _sut.GetSheetAsync(sheet.Id);

        Assert.Equal(sheet.Id, result.Id);
        Assert.Equal(sheet.Title, result.Title);
    }

    [Fact]
    public async Task UpdateSheetPageCountAsync_PersistsPageCount()
    {
        var sheet = await InsertSheetAsync(null);

        await _sut.UpdateSheetPageCountAsync(sheet, 12);

        var reloaded = await _sut.GetSheetAsync(sheet.Id);
        Assert.Equal(12, reloaded.PageCount);
    }

    [Fact]
    public async Task UpdateSheetLastViewedPageAsync_PersistsPageIndex()
    {
        var sheet = await InsertSheetAsync(null);

        await _sut.UpdateSheetLastViewedPageAsync(sheet, 4);

        var reloaded = await _sut.GetSheetAsync(sheet.Id);
        Assert.Equal(4, reloaded.LastViewedPageIndex);
    }

    private async Task<Sheet> InsertSheetAsync(int? folderId)
    {
        Directory.CreateDirectory(_storage.SheetsDirectory);
        var fileName = $"{Guid.NewGuid():N}.pdf";
        await File.WriteAllTextAsync(Path.Combine(_storage.SheetsDirectory, fileName), "fake-pdf");

        var sheet = new Sheet
        {
            FolderId = folderId,
            Title = "Test Sheet",
            FileName = fileName,
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };

        var database = new AppDatabase(_storage.DatabasePath);
        await database.Connection.InsertAsync(sheet);
        return sheet;
    }
}
