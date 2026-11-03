using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SheetViewerViewModelConcertTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AppDatabase _database;
    private readonly LibraryService _libraryService;
    private readonly FakePdfPageRenderer _renderer = new();
    private readonly FakeConcertModePreferenceService _concertMode = new();
    private readonly ConcertQueue _queue = new();
    private readonly SheetViewerViewModel _sut;

    public SheetViewerViewModelConcertTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(_database, _storage);
        _sut = CreateViewModel(new FakeReadingPreferenceService());
    }

    public void Dispose()
    {
        // Background preloads query the database: let them finish before it is torn down.
        foreach (var viewModel in _viewModels)
        {
            viewModel.CancelPreloadsAsync().GetAwaiter().GetResult();
        }

        _storage.Dispose();
    }

    private readonly List<SheetViewerViewModel> _viewModels = new();

    private SheetViewerViewModel CreateViewModel(IReadingPreferenceService preferences)
    {
        var viewModel = new SheetViewerViewModel(
            _libraryService,
            _storage,
            _renderer,
            new AnnotationService(_database),
            new BookmarkService(_database),
            preferences,
            new PageCropService(_database),
            concertQueue: _queue,
            concertMode: _concertMode);
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private async Task<Sheet> InsertSheetAsync(string title, int pageCount, int lastViewedPageIndex = 0)
    {
        var sheet = new Sheet
        {
            Title = title,
            FileName = title + ".pdf",
            PageCount = pageCount,
            LastViewedPageIndex = lastViewedPageIndex,
            DateAdded = DateTime.UtcNow
        };
        await _database.Connection.InsertAsync(sheet);
        return sheet;
    }

    private int RendersOf(string title) => _renderer.RenderedFiles.ToArray().Count(f => f.EndsWith(title + ".pdf"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition");
            await Task.Delay(10);
        }
    }

    private async Task<(Sheet A, Sheet B, Sheet C)> InsertQueueAsync(int aPages = 3, int bPages = 2, int cPages = 4)
    {
        var a = await InsertSheetAsync("A", aPages);
        var b = await InsertSheetAsync("B", bPages);
        var c = await InsertSheetAsync("C", cPages);
        _queue.SetQueue(new[] { a.Id, b.Id, c.Id });
        return (a, b, c);
    }

    [Fact]
    public async Task Next_OnLastPage_SwitchesToNextSheetFirstPage()
    {
        var (a, b, _) = await InsertQueueAsync();
        await _sut.LoadAsync(a.Id, 800, 1000);
        await _sut.GoToPageAsync(2);

        var switched = await _sut.TryGoToNextSheetAsync();

        Assert.True(switched);
        Assert.Equal("B", _sut.Title);
        Assert.Equal(b.Id, _sut.CurrentSheetId);
        Assert.Equal(2, _sut.PageCount);
        Assert.Equal(0, _sut.CurrentPageIndex);
        Assert.NotNull(_sut.CurrentPageImageBytes);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task Previous_OnFirstPage_SwitchesToPreviousSheetLastPage()
    {
        var (_, b, _) = await InsertQueueAsync();
        await _sut.LoadAsync(b.Id, 800, 1000);

        var switched = await _sut.TryGoToPreviousSheetAsync();

        Assert.True(switched);
        Assert.Equal("A", _sut.Title);
        Assert.Equal(3, _sut.PageCount);
        Assert.Equal(2, _sut.CurrentPageIndex);
        Assert.Equal(new byte[] { 2 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task Cross_NotOnEdgePage_DoesNothing()
    {
        var (a, _, _) = await InsertQueueAsync();
        await _sut.LoadAsync(a.Id, 800, 1000);

        Assert.False(_sut.CanCrossToNextSheet());
        Assert.False(await _sut.TryGoToNextSheetAsync());
        Assert.False(await _sut.TryGoToPreviousSheetAsync());
        Assert.Equal("A", _sut.Title);
    }

    [Fact]
    public async Task Cross_AtListEnds_IsNoOp()
    {
        var (a, _, c) = await InsertQueueAsync();
        await _sut.LoadAsync(c.Id, 800, 1000);
        await _sut.GoToPageAsync(3);
        Assert.False(await _sut.TryGoToNextSheetAsync());
        Assert.Equal("C", _sut.Title);

        await _sut.LoadAsync(a.Id, 800, 1000);
        Assert.False(await _sut.TryGoToPreviousSheetAsync());
        Assert.Equal("A", _sut.Title);
    }

    [Fact]
    public async Task Cross_WhenSheetNotInQueue_IsNoOp()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 1);
        _queue.Clear();
        await _sut.LoadAsync(a.Id, 800, 1000);

        Assert.False(await _sut.TryGoToNextSheetAsync());
    }

    [Fact]
    public async Task Cross_WhenDisabled_IsNoOpAndNothingPreloads()
    {
        _concertMode.Enabled = false;
        var (a, _, _) = await InsertQueueAsync(aPages: 1);
        await _sut.LoadAsync(a.Id, 800, 1000);

        Assert.False(_sut.CanCrossToNextSheet());
        Assert.False(await _sut.TryGoToNextSheetAsync());
        await Task.Delay(150);
        Assert.Equal(new[] { 0 }, _renderer.RenderedPageIndexes);
    }

    [Fact]
    public async Task Cross_InContinuousList_UsesDocumentEndFlag()
    {
        var (a, _, _) = await InsertQueueAsync();
        var sut = CreateViewModel(new FakeReadingPreferenceService { Mode = ReadingMode.VerticalContinuous });
        await sut.LoadAsync(a.Id, 800, 1000);

        Assert.False(sut.CanCrossToNextSheet());
        Assert.True(sut.CanCrossToNextSheet(atDocumentEnd: true));
        Assert.True(await sut.TryGoToNextSheetAsync(atDocumentEnd: true));
        Assert.Equal("B", sut.Title);
        Assert.Equal(2, sut.ContinuousPages.Count);
    }

    [Fact]
    public async Task NearEdge_PreloadsNextSheet_AndSwitchReusesItsRenders()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 1, bPages: 2);
        await _sut.LoadAsync(a.Id, 800, 1000);

        // The next sheet's pages 0 and 1 render in the background.
        await WaitUntilAsync(() => RendersOf("B") == 2);

        await _sut.TryGoToNextSheetAsync();
        await Task.Delay(150);

        Assert.Equal(2, RendersOf("B"));
        Assert.Equal(new byte[] { 0 }, _sut.CurrentPageImageBytes);
        Assert.Equal("B", _sut.Title);
    }

    [Fact]
    public async Task Preload_PreviousSheet_RendersItsLastPages()
    {
        var a = await InsertSheetAsync("A", 3);
        var b = await InsertSheetAsync("B", 1);
        _queue.SetQueue(new[] { a.Id, b.Id });
        await _sut.LoadAsync(b.Id, 800, 1000);

        // The previous sheet's last page, then the one before it.
        await WaitUntilAsync(() => RendersOf("A") == 2);
        var pages = _renderer.RenderedPageIndexes.ToArray().Zip(_renderer.RenderedFiles.ToArray())
            .Where(r => r.Second.EndsWith("A.pdf")).Select(r => r.First).ToArray();
        Assert.Equal(new[] { 2, 1 }, pages);

        await _sut.TryGoToPreviousSheetAsync();
        await Task.Delay(150);

        // Pages 2 and 1 are reused; only page 0 (within the prefetch radius) is new.
        Assert.Equal(3, RendersOf("A"));
        Assert.Equal(2, _sut.CurrentPageIndex);
        Assert.Equal(new byte[] { 2 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task FarFromEdge_DoesNotPreload()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 8);
        await _sut.LoadAsync(a.Id, 800, 1000);
        await Task.Delay(200);

        // Only the sheet's own page 0 and its prefetched neighbours 1 and 2.
        Assert.Equal(3, _renderer.RenderedPageIndexes.Count);
    }

    [Fact]
    public async Task MovingAwayFromTheEdge_DropsThePreload()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 8, bPages: 2);
        await _renderer_HoldAsync(async () =>
        {
            await _sut.LoadAsync(a.Id, 800, 1000);
            await _sut.GoToPageAsync(6);
            // The held renders keep the preload queued; moving away cancels it.
            await _sut.GoToPageAsync(1);
        });

        await Task.Delay(200);

        Assert.DoesNotContain(_renderer.RenderedFiles, f => f.EndsWith("B.pdf"));
    }

    private async Task _renderer_HoldAsync(Func<Task> action)
    {
        // Current-sheet renders stay in flight, so the low-priority preload
        // cannot start; releasing them afterwards lets the test finish.
        var gate = new TaskCompletionSource();
        _renderer.RenderGate = gate;
        var run = action();
        await Task.Delay(100);
        gate.SetResult();
        await run;
        _renderer.RenderGate = null;
    }

    [Fact]
    public async Task ViewportChange_DiscardsStalePreload()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 1, bPages: 2);
        await _sut.LoadAsync(a.Id, 800, 1000);
        await WaitUntilAsync(() => RendersOf("B") == 2);

        await _sut.LoadAsync(a.Id, 400, 500);

        // The neighbour is preloaded again at the new size, not reused.
        await WaitUntilAsync(() => RendersOf("B") == 4);
        await _sut.TryGoToNextSheetAsync();
        await Task.Delay(150);

        Assert.Equal(4, RendersOf("B"));
        Assert.Equal("B", _sut.Title);
    }

    [Fact]
    public async Task Switch_SavesLastViewedPageOfOldSheet_AndStartsNewSheetAtFirstPage()
    {
        var a = await InsertSheetAsync("A", 3, lastViewedPageIndex: 2);
        var b = await InsertSheetAsync("B", 2, lastViewedPageIndex: 1);
        _queue.SetQueue(new[] { a.Id, b.Id });
        await _sut.LoadAsync(a.Id, 800, 1000);
        Assert.Equal(2, _sut.CurrentPageIndex);

        // Something else moved the stored value; leaving must write the real page back.
        await _libraryService.UpdateSheetLastViewedPageAsync(await _libraryService.GetSheetAsync(a.Id), 0);

        Assert.True(await _sut.TryGoToNextSheetAsync());

        Assert.Equal(2, (await _libraryService.GetSheetAsync(a.Id)).LastViewedPageIndex);
        Assert.Equal(0, (await _libraryService.GetSheetAsync(b.Id)).LastViewedPageIndex);
    }

    [Fact]
    public async Task Switch_ResetsPerSheetState_AndRaisesEvent()
    {
        var (a, b, _) = await InsertQueueAsync(aPages: 1);
        await new BookmarkService(_database).AddBookmarkAsync(a.Id, 0, "mark");
        await _sut.LoadAsync(a.Id, 800, 1000);
        Assert.Single(_sut.Bookmarks);
        bool? toLast = null;
        _sut.SheetSwitched += last => toLast = last;

        await _sut.TryGoToNextSheetAsync();

        Assert.False(toLast);
        Assert.Empty(_sut.Bookmarks);
        Assert.Empty(_sut.CurrentPageAnnotations);
        Assert.Equal(b.Id, _sut.CurrentSheetId);
    }

    [Fact]
    public async Task Switch_WithoutPreload_StillWorks()
    {
        var (a, _, _) = await InsertQueueAsync(aPages: 3);
        await _sut.LoadAsync(a.Id, 800, 1000);
        await _sut.GoToPageAsync(2);

        // Immediately, before any preload could finish.
        Assert.True(await _sut.TryGoToNextSheetAsync());
        Assert.Equal("B", _sut.Title);
        Assert.NotNull(_sut.CurrentPageImageBytes);
    }
}
