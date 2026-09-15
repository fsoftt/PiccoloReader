using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SheetViewerViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AppDatabase _database;
    private readonly LibraryService _libraryService;
    private readonly FakePdfPageRenderer _renderer = new();
    private readonly SheetViewerViewModel _sut;

    public SheetViewerViewModelTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(_database, _storage);
        _sut = new SheetViewerViewModel(_libraryService, _storage, _renderer);
    }

    public void Dispose() => _storage.Dispose();

    private async Task<Sheet> InsertSheetAsync(int pageCount)
    {
        var sheet = new Sheet
        {
            FolderId = null,
            Title = "My Piece",
            FileName = "irrelevant.pdf",
            PageCount = pageCount,
            DateAdded = DateTime.UtcNow
        };
        await _database.Connection.InsertAsync(sheet);
        return sheet;
    }

    [Fact]
    public async Task LoadAsync_SetsTitleAndPageCountFromSheet()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal("My Piece", _sut.Title);
        Assert.Equal(5, _sut.PageCount);
        Assert.Equal(0, _sut.CurrentPageIndex);
        Assert.Equal(1, _sut.CurrentPageDisplay);
    }

    [Fact]
    public async Task LoadAsync_PageCountZero_ComputesAndPersistsFromRenderer()
    {
        _renderer.PageCountToReturn = 7;
        var sheet = await InsertSheetAsync(pageCount: 0);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(7, _sut.PageCount);
        Assert.Equal(1, _renderer.GetPageCountCallCount);

        var reloaded = await _libraryService.GetSheetAsync(sheet.Id);
        Assert.Equal(7, reloaded.PageCount);
    }

    [Fact]
    public async Task LoadAsync_PageCountAlreadyKnown_DoesNotCallRendererForPageCount()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(0, _renderer.GetPageCountCallCount);
    }

    [Fact]
    public async Task LoadAsync_RendersFirstPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(new byte[] { 0 }, _sut.CurrentPageImageBytes);
        Assert.Equal(new List<int> { 0 }, _renderer.RenderedPageIndexes);
    }

    [Fact]
    public async Task NextPageAsync_AdvancesPageAndRendersIt()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(1, _sut.CurrentPageIndex);
        Assert.Equal(2, _sut.CurrentPageDisplay);
        Assert.Equal(new byte[] { 1 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task NextPageCommand_AtLastPage_CannotExecute()
    {
        var sheet = await InsertSheetAsync(pageCount: 2);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.False(_sut.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageCommand_AtFirstPage_CannotExecute()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.False(_sut.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageAsync_AfterNext_GoesBackToFirstPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.NextPageCommand.ExecuteAsync(null);

        await _sut.PreviousPageCommand.ExecuteAsync(null);

        Assert.Equal(0, _sut.CurrentPageIndex);
        Assert.False(_sut.PreviousPageCommand.CanExecute(null));
    }
}
