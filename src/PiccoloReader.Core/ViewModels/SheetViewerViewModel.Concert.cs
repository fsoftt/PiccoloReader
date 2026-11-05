using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.ViewModels;

// Concert mode: "next" on the last page opens the next score of the list the
// viewer was opened from (and "previous" on the first page the previous one),
// in place, with the neighbouring sheets preloaded in the background.
public partial class SheetViewerViewModel
{
    // Pages of a neighbour sheet rendered ahead of time: the one the reader
    // lands on and the one after it. Kept at two to cap memory (a next and a
    // previous preload can exist at once).
    private const int PreloadPageCount = 2;

    // Poll interval of a preload waiting for the renderer to be free.
    private static readonly TimeSpan PreloadIdlePoll = TimeSpan.FromMilliseconds(25);

    // Bumped whenever a sheet is (re)opened. Work started for an earlier
    // sheet compares it after each await and drops its result.
    private int _sheetVersion;

    // Current-sheet renders queued or running (see RenderPageGatedAsync).
    private int _currentRenderDemand;

    private bool _isSwitchingSheet;
    private SheetPreload? _nextPreload;
    private SheetPreload? _previousPreload;

    /// <summary>
    /// Raised during an in-place sheet switch, after the new sheet's state is
    /// set and before its page is shown. The argument is true when the switch
    /// lands on the new sheet's last page (going back).
    /// </summary>
    public event Action<bool>? SheetSwitched;

    public int CurrentSheetId => _sheet?.Id ?? 0;

    private bool ConcertEnabled => _concertMode?.Enabled == true && _concertQueue is not null;

    /// <summary>
    /// True when a "next" request should switch to the next score instead of
    /// being a no-op: concert mode on, on the last page (or, in the
    /// continuous list, scrolled to the end: <paramref name="atDocumentEnd"/>)
    /// and a next score exists.
    /// </summary>
    public bool CanCrossToNextSheet(bool atDocumentEnd = false) => CrossTarget(next: true, atDocumentEnd) is not null;

    public bool CanCrossToPreviousSheet(bool atDocumentStart = false) => CrossTarget(next: false, atDocumentStart) is not null;

    /// <summary>
    /// The score a pull past the end (start) of the continuous list would
    /// open, or null when none applies.
    /// </summary>
    public int? ConcertPullTarget(bool next) => CrossTarget(next, atEdgeOfContinuousList: true);

    /// <summary>
    /// True when concert mode is on and the open sheet came from a list with a
    /// next or previous score (what the viewer tutorial explains).
    /// </summary>
    public bool HasConcertNeighbour =>
        ConcertEnabled && _sheet is not null
        && (_concertQueue!.NextAfter(_sheet.Id) is not null || _concertQueue.PreviousBefore(_sheet.Id) is not null);

    private int? CrossTarget(bool next, bool atEdgeOfContinuousList)
    {
        if (!ConcertEnabled || _sheet is null || _isSwitchingSheet || IsContinuousEditing || PageCount <= 0)
        {
            return null;
        }

        var onEdgePage = next ? CurrentPageIndex >= PageCount - 1 : CurrentPageIndex <= 0;
        if (!onEdgePage && !(IsContinuousReading && atEdgeOfContinuousList))
        {
            return null;
        }

        return next ? _concertQueue!.NextAfter(_sheet.Id) : _concertQueue!.PreviousBefore(_sheet.Id);
    }

    /// <summary>Switches to the next score, showing its first page. False when not applicable.</summary>
    public Task<bool> TryGoToNextSheetAsync(bool atDocumentEnd = false) =>
        CrossTarget(next: true, atDocumentEnd) is { } id
            ? SwitchSheetAsync(id, StartPage.First)
            : Task.FromResult(false);

    /// <summary>Switches to the previous score, showing its last page. False when not applicable.</summary>
    public Task<bool> TryGoToPreviousSheetAsync(bool atDocumentStart = false) =>
        CrossTarget(next: false, atDocumentStart) is { } id
            ? SwitchSheetAsync(id, StartPage.Last)
            : Task.FromResult(false);

    private async Task<bool> SwitchSheetAsync(int targetSheetId, StartPage startPage)
    {
        if (_isSwitchingSheet || _sheet is null)
        {
            return false;
        }

        _isSwitchingSheet = true;
        try
        {
            var leaving = _sheet;
            var leavingPage = CurrentPageIndex;

            // The score left behind keeps the page the user finished on, and
            // its cached thumbnail follows. Runs alongside the switch.
            SaveCurrentPageThumbnail();
            var saveLeaving = _libraryService.UpdateSheetLastViewedPageAsync(leaving, leavingPage);

            var preload = TakePreload(targetSheetId, startPage == StartPage.Last);
            IsContinuousEditing = false;
            await LoadCoreAsync(targetSheetId, _targetWidthPx, _targetHeightPx, startPage, preload, switching: true);

            try
            {
                await saveLeaving;
                await PersistLastViewedPageAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Saving last viewed page failed: {ex.Message}");
            }

            return true;
        }
        finally
        {
            _isSwitchingSheet = false;
        }
    }

    // --- Preloading -------------------------------------------------------

    private sealed record PreloadedSheet(
        Sheet Sheet, string FilePath, int PageCount, Dictionary<int, PageCrop> Crops, List<Bookmark> Bookmarks);

    private sealed class SheetPreload
    {
        public SheetPreload(int sheetId, bool lastPage, int width, int height)
        {
            SheetId = sheetId;
            LastPage = lastPage;
            Width = width;
            Height = height;
        }

        public int SheetId { get; }

        public bool LastPage { get; }

        public int Width { get; }

        public int Height { get; }

        public CancellationTokenSource Cancellation { get; } = new();

        // Set when the viewer switches to this sheet: its renders are then
        // current-sheet work and stop yielding to other renders.
        public volatile bool Promoted;

        // Completes once the sheet's data is loaded and its renders have been
        // started (Renders is filled by then); null when loading failed.
        public Task<PreloadedSheet?> Data { get; set; } = Task.FromResult<PreloadedSheet?>(null);

        public Dictionary<int, Task<byte[]?>> Renders { get; } = new();
    }

    // Starts, keeps or drops the next/previous preloads for the current
    // position. Cheap and idempotent: called after every page change.
    private void UpdatePreloads()
    {
        if (!ConcertEnabled || _sheet is null || PageCount <= 0)
        {
            DiscardPreloads();
            return;
        }

        var id = _sheet.Id;
        var nearEnd = PageCount == 1 || CurrentPageIndex >= PageCount - 2;
        var nearStart = PageCount == 1 || CurrentPageIndex <= 1;
        _nextPreload = EnsurePreload(_nextPreload, nearEnd ? _concertQueue!.NextAfter(id) : null, lastPage: false);
        _previousPreload = EnsurePreload(_previousPreload, nearStart ? _concertQueue!.PreviousBefore(id) : null, lastPage: true);
    }

    private SheetPreload? EnsurePreload(SheetPreload? current, int? targetSheetId, bool lastPage)
    {
        if (targetSheetId is not { } target)
        {
            current?.Cancellation.Cancel();
            return null;
        }

        if (current is not null
            && current.SheetId == target
            && current.Width == _targetWidthPx
            && current.Height == _targetHeightPx
            && !current.Cancellation.IsCancellationRequested)
        {
            return current;
        }

        current?.Cancellation.Cancel();
        var preload = new SheetPreload(target, lastPage, _targetWidthPx, _targetHeightPx);
        preload.Data = PreloadSheetAsync(preload);
        TrackPreload(preload.Data);
        return preload;
    }

    private readonly List<Task> _preloadsInFlight = new();

    private void TrackPreload(Task data)
    {
        lock (_preloadsInFlight)
        {
            _preloadsInFlight.Add(data);
        }

        _ = data.ContinueWith(
            finished =>
            {
                lock (_preloadsInFlight)
                {
                    _preloadsInFlight.Remove(finished);
                }
            },
            TaskScheduler.Default);
    }

    /// <summary>Drops the neighbour preloads; call when the viewer is left.</summary>
    public void CancelPreloads() => DiscardPreloads();

    /// <summary>Drops the preloads and waits for their database work to wind down.</summary>
    public async Task CancelPreloadsAsync()
    {
        DiscardPreloads();
        Task[] pending;
        lock (_preloadsInFlight)
        {
            pending = _preloadsInFlight.ToArray();
        }

        await Task.WhenAll(pending);
    }

    private void DiscardPreloads()
    {
        _nextPreload?.Cancellation.Cancel();
        _previousPreload?.Cancellation.Cancel();
        _nextPreload = null;
        _previousPreload = null;
    }

    // Hands over the preload for the sheet about to open, if it is still
    // valid for the current render size. Every other preload is discarded by
    // the load that follows.
    private SheetPreload? TakePreload(int sheetId, bool lastPage)
    {
        var slot = lastPage ? _previousPreload : _nextPreload;
        if (slot is null
            || slot.SheetId != sheetId
            || slot.Width != _targetWidthPx
            || slot.Height != _targetHeightPx
            || slot.Cancellation.IsCancellationRequested)
        {
            return null;
        }

        slot.Promoted = true;
        if (lastPage)
        {
            _previousPreload = null;
        }
        else
        {
            _nextPreload = null;
        }

        return slot;
    }

    private async Task<PreloadedSheet?> PreloadSheetAsync(SheetPreload preload)
    {
        var token = preload.Cancellation.Token;
        try
        {
            var sheet = await _libraryService.GetSheetAsync(preload.SheetId);
            var filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);
            var pageCount = sheet.PageCount;
            if (pageCount <= 0)
            {
                pageCount = await _pdfPageRenderer.GetPageCountAsync(filePath);
                await _libraryService.UpdateSheetPageCountAsync(sheet, pageCount);
            }

            token.ThrowIfCancellationRequested();
            var crops = await _pageCropService.GetCropsAsync(preload.SheetId);
            var bookmarks = (await _bookmarkService.GetBookmarksAsync(preload.SheetId)).ToList();
            token.ThrowIfCancellationRequested();

            // The page the reader lands on first, then its neighbour.
            for (var i = 0; i < PreloadPageCount && i < pageCount; i++)
            {
                var pageIndex = preload.LastPage ? pageCount - 1 - i : i;
                var crop = crops.TryGetValue(pageIndex, out var found) ? found : PageCrop.Full;
                preload.Renders[pageIndex] = RenderPreloadAsync(preload, filePath, pageIndex, crop);
            }

            return new PreloadedSheet(sheet, filePath, pageCount, crops, bookmarks);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sheet preload failed: {ex.Message}");
            return null;
        }
    }

    // Preload renders use the same gate as page renders (one full-size render
    // in memory/CPU at a time, and the platform renderer is not meant to run
    // concurrently) but only take it while no current-sheet render is queued
    // or running, so a preload never delays the sheet being read. The wait is
    // skipped once the sheet is opened (Promoted): the render is then wanted
    // now. A preload already holding the gate delays a later current render by
    // at most its own duration.
    private async Task<byte[]?> RenderPreloadAsync(SheetPreload preload, string filePath, int pageIndex, PageCrop crop)
    {
        var token = preload.Cancellation.Token;
        try
        {
            while (!preload.Promoted && Volatile.Read(ref _currentRenderDemand) > 0)
            {
                await Task.Delay(PreloadIdlePoll, token);
            }

            await _renderGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested && !preload.Promoted)
                {
                    return null;
                }

                return await _pdfPageRenderer.RenderPageAsync(
                    filePath, pageIndex, preload.Width, preload.Height, crop.IsFull ? null : crop);
            }
            finally
            {
                _renderGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
