using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SheetViewerViewModel : ObservableObject
{
    private const double DefaultIconWidth = 0.12;

    // Pages within this distance of the current one are rendered ahead of
    // time and kept in memory, so turning to a neighbouring page shows it
    // instantly instead of waiting on the PDF renderer.
    private const int PrefetchRadius = 2;

    private readonly LibraryService _libraryService;
    private readonly IAppStorageProvider _storageProvider;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly AnnotationService _annotationService;
    private readonly BookmarkService _bookmarkService;
    private readonly PageCropService _pageCropService;
    private readonly IAdsPreferenceService _adsPreferenceService;
    private readonly IReadingPreferenceService _readingPreferenceService;

    // One render task per page index - current page plus prefetched
    // neighbours. Renders run one at a time through _renderGate (each
    // full-screen render is memory heavy), and a queued render whose page
    // has fallen out of the window by the time it gets its turn is
    // skipped, so flicking quickly through many pages never leaves a
    // backlog of renders nobody will see.
    private readonly Dictionary<int, Task<byte[]?>> _pageRenders = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    // Detail renders (the sharp zoomed-in view) have their own gate so they
    // never hold up page renders/prefetch, and only one is in memory at once.
    private readonly SemaphoreSlim _detailGate = new(1, 1);

    // Pages worth rendering/keeping right now: current page +/-
    // PrefetchRadius in the paged modes, the visible pages plus one on
    // each side in continuous mode.
    private int _keepFirst;
    private int _keepLast;

    // Undo/redo history of the page being edited. Each page keeps its own
    // history for as long as the document stays open, so leaving a page
    // (or the continuous-mode editor) and coming back can still undo.
    // Annotations are re-read from the database on every page load, so the
    // instances the stored actions reference are remembered per page and
    // reused when the page is loaded again.
    private sealed class PageHistory
    {
        public Stack<IUndoableAction> Undo = new();
        public Stack<IUndoableAction> Redo = new();
        public Dictionary<int, Annotation> Instances = new();
    }

    private readonly Dictionary<int, PageHistory> _histories = new();
    private Stack<IUndoableAction> _undoStack = new();
    private Stack<IUndoableAction> _redoStack = new();
    private int _historyPageIndex = -1;
    private List<IUndoableAction>? _eraseBatch;

    // Changes made (and not undone) since edit mode was last activated, so
    // Back can undo just those and never reach edits from earlier sessions.
    private int _editSessionChanges;

    // True when Back should undo a change instead of leaving edit mode.
    public bool HasEditSessionChanges => _editSessionChanges > 0 && CanUndo;

    // Called when edit mode is activated: earlier history stays undoable from
    // the editing bar but is no longer counted for Back.
    public void BeginEditSession() => _editSessionChanges = 0;

    private Sheet? _sheet;
    private string _filePath = string.Empty;
    private int _targetWidthPx;
    private int _targetHeightPx;

    // The portrait reading area (dp) legacy annotations were normalized to: a
    // stable value derived by the view from the screen, not from the current
    // layout (toolbar shown/hidden, rotation), so a legacy row converts to
    // the same result whichever page loads it and whenever. (0, 0) while
    // unknown: legacy rows are then converted for display only (against the
    // render target) and not saved, so a guess is never persisted. Only its
    // shape matters.
    public (double Width, double Height) LegacyReferenceSize { get; set; }

    // Height / width of the page shown in the single-page viewer (0 while
    // unknown). Annotations are normalized to that page rectangle.
    public double CurrentPageAspectRatio { get; private set; }

    // Crops of the open sheet's pages (pages not in here are shown whole).
    private Dictionary<int, PageCrop> _crops = new();

    // The visible part of the page shown in the single-page viewer. The
    // aspect ratio above and every annotation stay relative to the FULL page.
    [ObservableProperty]
    private PageCrop _currentPageCrop = PageCrop.Full;

    public PageCrop GetPageCrop(int pageIndex) =>
        _crops.TryGetValue(pageIndex, out var crop) ? crop : PageCrop.Full;

    public SheetViewerViewModel(
        LibraryService libraryService,
        IAppStorageProvider storageProvider,
        IPdfPageRenderer pdfPageRenderer,
        AnnotationService annotationService,
        BookmarkService bookmarkService,
        IAdsPreferenceService adsPreferenceService,
        IReadingPreferenceService readingPreferenceService,
        PageCropService pageCropService,
        IPageThumbnailStore? thumbnails = null,
        TimeProvider? timeProvider = null)
    {
        _pageCropService = pageCropService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _thumbnails = thumbnails;
        _libraryService = libraryService;
        _storageProvider = storageProvider;
        _pdfPageRenderer = pdfPageRenderer;
        _annotationService = annotationService;
        _bookmarkService = bookmarkService;
        _adsPreferenceService = adsPreferenceService;
        _readingPreferenceService = readingPreferenceService;
        _readingMode = readingPreferenceService.GetReadingMode();
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyPropertyChangedFor(nameof(CurrentPageDisplay))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _currentPageIndex;

    [ObservableProperty]
    private byte[]? _currentPageImageBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isLoading;

    // True while a cached thumbnail of the opening page stands in for the
    // page that is still rendering.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingSpinner))]
    private bool _isShowingThumbnail;

    // Safety net: if IsLoading stays true although the current page is
    // already on screen, the spinner is stuck over a rendered page.
    private static readonly TimeSpan SpinnerWatchdogInitialDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SpinnerWatchdogRecheckDelay = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _spinnerWatchdog;

    partial void OnIsLoadingChanged(bool value)
    {
        if (value)
        {
            StartSpinnerWatchdog();
        }
        else
        {
            StopSpinnerWatchdog();
        }
    }

    // Also called by the view when it is shown again while still loading.
    public void StartSpinnerWatchdog()
    {
        if (_spinnerWatchdog is not null || !IsLoading)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _spinnerWatchdog = cts;
        _ = RunSpinnerWatchdogAsync(cts.Token);
    }

    // Also called when leaving the viewer.
    public void StopSpinnerWatchdog()
    {
        _spinnerWatchdog?.Cancel();
        _spinnerWatchdog = null;
    }

    // Continuations stay on the calling (UI) context: they touch properties.
    private async Task RunSpinnerWatchdogAsync(CancellationToken token)
    {
        var delay = SpinnerWatchdogInitialDelay;
        while (true)
        {
            try
            {
                await Task.Delay(delay, _timeProvider, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested || !IsLoading)
            {
                return;
            }

            if (IsCurrentPageDisplayed())
            {
                Console.WriteLine($"SpinnerWatchdog: clearing stuck spinner (CurrentPageIndex={CurrentPageIndex}, ReadingMode={ReadingMode}, IsShowingThumbnail={IsShowingThumbnail})");
                IsLoading = false;
                return;
            }

            delay = SpinnerWatchdogRecheckDelay;
        }
    }

    private bool IsCurrentPageDisplayed()
    {
        if (IsContinuousReading)
        {
            return CurrentPageIndex >= 0
                && CurrentPageIndex < ContinuousPages.Count
                && ContinuousPages[CurrentPageIndex].ImageBytes is not null;
        }

        return CurrentPageImageBytes is not null
            && !IsShowingThumbnail
            && !ReferenceEquals(CurrentPageImageBytes, OpeningThumbnailBytes);
    }

    // The spinner would sit on top of the thumbnail, so it is hidden then.
    public bool ShowLoadingSpinner => IsLoading && !IsShowingThumbnail;

    // The opening thumbnail, kept under the page image after the full render
    // is set so the view can leave it showing until that image has decoded
    // (swapping an Image source blanks it for a moment). The view clears it.
    [ObservableProperty]
    private byte[]? _openingThumbnailBytes;

    private readonly IPageThumbnailStore? _thumbnails;
    private bool _saveThumbnailAfterRender;
    private int _thumbnailPageIndex = -1;
    private int _openVersion;
    private bool _thumbnailApplicable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedAnnotationCommand))]
    private Annotation? _selectedAnnotation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDrawingToolActive))]
    private AnnotationTool _activeTool = AnnotationTool.MusicIcons;

    [ObservableProperty]
    private string _pencilColorHex = "#000000";

    [ObservableProperty]
    private double _pencilStrokeWidth = 0.008;

    [ObservableProperty]
    private double _eraserRadius = 0.05;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private bool _showAdsBanner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerticalPaged))]
    [NotifyPropertyChangedFor(nameof(IsContinuousReading))]
    private ReadingMode _readingMode;

    public bool IsVerticalPaged => ReadingMode == ReadingMode.VerticalPaged;

    public bool IsContinuousReading => ReadingMode == ReadingMode.VerticalContinuous;

    public ObservableCollection<ContinuousPage> ContinuousPages { get; } = new();

    // True while one page of the continuous list is open in the single-page
    // editor (tools, selection, zoom). Page turning is disabled meanwhile -
    // the user leaves the editor to scroll to another page.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _isContinuousEditing;

    public bool IsDrawingToolActive => ActiveTool != AnnotationTool.MusicIcons;

    public ObservableCollection<Annotation> CurrentPageAnnotations { get; } = new();

    public ObservableCollection<Bookmark> Bookmarks { get; } = new();

    public int CurrentPageDisplay => CurrentPageIndex + 1;

    public string PageIndicatorText => string.Format(AppStrings.PageIndicatorFormat, CurrentPageDisplay, PageCount);

    public async Task LoadAsync(int sheetId, int targetWidthPx, int targetHeightPx)
    {
        ShowAdsBanner = _adsPreferenceService.GetSupportWithAdsEnabled();
        ReadingMode = _readingPreferenceService.GetReadingMode();
        _pageRenders.Clear();
        ClearUndoHistory();
        OpeningThumbnailBytes = null;

        IsLoading = true;
        try
        {
            _targetWidthPx = targetWidthPx;
            _targetHeightPx = targetHeightPx;

            var sheet = await _libraryService.GetSheetAsync(sheetId);
            _sheet = sheet;
            Title = sheet.Title;
            _filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);

            var pageCount = sheet.PageCount;
            if (pageCount <= 0)
            {
                pageCount = await _pdfPageRenderer.GetPageCountAsync(_filePath);
                await _libraryService.UpdateSheetPageCountAsync(sheet, pageCount);
            }

            PageCount = pageCount;
            CurrentPageIndex = pageCount > 0
                ? Math.Clamp(sheet.LastViewedPageIndex, 0, pageCount - 1)
                : 0;

            // The cached thumbnail loads in parallel with the heavy work and
            // never delays it: it is applied only if it arrives before the
            // full render of the opening page.
            var openId = ++_openVersion;
            _saveThumbnailAfterRender = _thumbnails is not null && pageCount > 0;
            _thumbnailPageIndex = CurrentPageIndex;
            _thumbnailApplicable = pageCount > 0 && _thumbnails is not null;
            if (_thumbnailApplicable)
            {
                _ = ApplyOpeningThumbnailAsync(sheetId, CurrentPageIndex, openId);
            }

            _crops = await _pageCropService.GetCropsAsync(sheetId);

            Bookmarks.Clear();
            foreach (var bookmark in await _bookmarkService.GetBookmarksAsync(sheetId))
            {
                Bookmarks.Add(bookmark);
            }

            ContinuousPages.Clear();
            var estimatedAspectRatio = targetWidthPx > 0 && targetHeightPx > 0
                ? (double)targetHeightPx / targetWidthPx
                : Math.Sqrt(2);
            for (var i = 0; i < pageCount; i++)
            {
                var crop = GetPageCrop(i);
                ContinuousPages.Add(new ContinuousPage(i, crop.CroppedAspectRatio(estimatedAspectRatio)) { Crop = crop });
            }

            if (IsContinuousReading)
            {
                await UpdateContinuousViewportAsync(CurrentPageIndex, CurrentPageIndex, CurrentPageIndex);
            }
            else
            {
                await LoadCurrentPageAsync();
            }
        }
        finally
        {
            if (IsShowingThumbnail && !IsContinuousReading)
            {
                // The render failed: do not leave the stand-in as if it were the page.
                CurrentPageImageBytes = null;
                OpeningThumbnailBytes = null;
            }

            _thumbnailApplicable = false;
            IsShowingThumbnail = false;
            _saveThumbnailAfterRender = false;
            IsLoading = false;
        }
    }

    public async Task GoToPageAsync(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount || pageIndex == CurrentPageIndex)
        {
            return;
        }

        // A jump (page indicator, bookmark) while a continuous-mode page
        // is open in the editor goes back to the list first.
        if (IsContinuousEditing)
        {
            await EndContinuousEditAsync();
        }

        CurrentPageIndex = pageIndex;
        if (IsContinuousReading)
        {
            await UpdateContinuousViewportAsync(pageIndex, pageIndex, pageIndex);
        }
        else
        {
            await LoadCurrentPageAsync();
        }

        await PersistLastViewedPageAsync();
    }

    // Called by the view as the continuous list scrolls. The page nearest
    // the middle of the screen becomes the current page (page indicator,
    // bookmarks, last-viewed position); the visible pages and one on each
    // side get rendered, and pages that scrolled far away are released.
    public async Task UpdateContinuousViewportAsync(int firstVisibleIndex, int lastVisibleIndex, int centerIndex)
    {
        if (!IsContinuousReading || IsContinuousEditing || PageCount <= 0)
        {
            return;
        }

        var last = PageCount - 1;
        centerIndex = Math.Clamp(centerIndex, 0, last);
        firstVisibleIndex = Math.Clamp(Math.Min(firstVisibleIndex, centerIndex), 0, last);
        lastVisibleIndex = Math.Clamp(Math.Max(lastVisibleIndex, centerIndex), 0, last);

        var pageChanged = centerIndex != CurrentPageIndex;
        if (pageChanged)
        {
            CurrentPageIndex = centerIndex;
        }

        _keepFirst = Math.Max(0, firstVisibleIndex - 1);
        _keepLast = Math.Min(last, lastVisibleIndex + 1);

        foreach (var page in ContinuousPages)
        {
            if ((page.PageIndex < _keepFirst || page.PageIndex > _keepLast) && page.IsLoadRequested)
            {
                page.IsLoadRequested = false;
                page.ImageBytes = null;
                page.Annotations = Array.Empty<Annotation>();
            }
        }

        EvictDistantPages();

        // Center page first so it wins the render queue, then the rest of
        // the window in reading order.
        var loads = new List<Task>();
        foreach (var index in Enumerable.Range(_keepFirst, _keepLast - _keepFirst + 1).OrderBy(i => i == centerIndex ? 0 : 1))
        {
            var page = ContinuousPages[index];
            if (!page.IsLoadRequested)
            {
                loads.Add(LoadContinuousPageAsync(page));
            }
        }

        if (pageChanged)
        {
            loads.Add(PersistLastViewedPageAsync());
        }

        await Task.WhenAll(loads);
    }

    private async Task LoadContinuousPageAsync(ContinuousPage page)
    {
        page.IsLoadRequested = true;

        page.Crop = GetPageCrop(page.PageIndex);
        var imageBytes = await GetPageImageAsync(page.PageIndex);
        if (!page.IsLoadRequested)
        {
            return;
        }

        if (imageBytes is null)
        {
            page.IsLoadRequested = false;
            return;
        }

        if (PngDimensions.TryRead(imageBytes, out var width, out var height))
        {
            page.AspectRatio = (double)height / width;
        }

        page.ImageBytes = imageBytes;
        if (page.PageIndex == _thumbnailPageIndex)
        {
            _thumbnailApplicable = false;
            IsShowingThumbnail = false;
        }

        SaveOpeningThumbnail(page.PageIndex, imageBytes);

        if (_sheet is not null)
        {
            var annotations = await LoadPageAnnotationsAsync(page.PageIndex, page.FullAspectRatio);
            if (page.IsLoadRequested)
            {
                page.Annotations = annotations.ToList();
            }
        }
    }

    public async Task AddBookmarkAsync(int pageIndex, string? name = null)
    {
        if (_sheet is null)
        {
            return;
        }

        var bookmark = await _bookmarkService.AddBookmarkAsync(_sheet.Id, pageIndex, name);
        Bookmarks.Add(bookmark);
    }

    private bool CanGoToNextPage() => !IsContinuousEditing && CurrentPageIndex < PageCount - 1;

    private bool CanGoToPreviousPage() => !IsContinuousEditing && CurrentPageIndex > 0;

    // Opens one page of the continuous list in the single-page editor.
    public async Task BeginContinuousEditAsync(int pageIndex)
    {
        if (!IsContinuousReading || PageCount <= 0)
        {
            return;
        }

        CurrentPageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        IsContinuousEditing = true;
        await LoadCurrentPageAsync();
    }

    // Back to scrolling: the edited page's annotations are re-read so the
    // list shows the changes.
    public async Task EndContinuousEditAsync()
    {
        if (!IsContinuousEditing)
        {
            return;
        }

        IsContinuousEditing = false;
        SelectedAnnotation = null;
        ActiveTool = AnnotationTool.MusicIcons;

        if (_sheet is not null && CurrentPageIndex < ContinuousPages.Count)
        {
            var page = ContinuousPages[CurrentPageIndex];
            var annotations = await LoadPageAnnotationsAsync(page.PageIndex, page.FullAspectRatio);
            page.Annotations = annotations.ToList();
        }
    }

    private bool CanDeleteSelectedAnnotation() => SelectedAnnotation is not null;

    // Cycles Horizontal -> VerticalPaged -> VerticalContinuous.
    [RelayCommand]
    private async Task CycleReadingModeAsync()
    {
        var previousMode = ReadingMode;
        IsContinuousEditing = false;
        ReadingMode = ReadingMode switch
        {
            ReadingMode.Horizontal => ReadingMode.VerticalPaged,
            ReadingMode.VerticalPaged => ReadingMode.VerticalContinuous,
            _ => ReadingMode.Horizontal
        };
        _readingPreferenceService.SetReadingMode(ReadingMode);

        if (IsContinuousReading)
        {
            // Drop any in-progress selection/tool (editing in continuous
            // mode goes through BeginContinuousEditAsync), and reload
            // annotations since they may have been edited in a paged mode
            // since the pages were last shown.
            SelectedAnnotation = null;
            ActiveTool = AnnotationTool.MusicIcons;
            foreach (var page in ContinuousPages)
            {
                page.IsLoadRequested = false;
            }

            await UpdateContinuousViewportAsync(CurrentPageIndex, CurrentPageIndex, CurrentPageIndex);
        }
        else if (previousMode == ReadingMode.VerticalContinuous)
        {
            // CurrentPageIndex moved while scrolling without the single
            // page view following it.
            await LoadCurrentPageAsync();
        }
    }

    // AllowConcurrentExecutions: page turns must not wait for the previous
    // page's render to finish - otherwise the command stays disabled while
    // a render is in flight and quick taps/swipes are silently dropped.
    [RelayCommand(CanExecute = nameof(CanGoToNextPage), AllowConcurrentExecutions = true)]
    private async Task NextPageAsync()
    {
        CurrentPageIndex++;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage), AllowConcurrentExecutions = true)]
    private async Task PreviousPageAsync()
    {
        CurrentPageIndex--;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

    // The part of the current page on screen (full-page coordinates), set by
    // the view while zoomed or panned; null means the whole cropped page.
    public PageCrop? VisibleArea { get; set; }

    [RelayCommand]
    private async Task PlaceIconAsync(string iconKey)
    {
        if (_sheet is null)
        {
            return;
        }

        var icon = MusicIconCatalog.FindByKey(iconKey);
        var aspectRatio = icon?.AspectRatio ?? 1.0;

        var width = DefaultIconWidth;
        // Width and height are fractions of the page's own width and height.
        var height = width / aspectRatio;
        if (CurrentPageAspectRatio > 0)
        {
            height /= CurrentPageAspectRatio;
        }

        // Sized relative to the cropped part, placed at the middle of what is
        // on screen (the zoomed-in area when there is one) and kept inside
        // the cropped part.
        var crop = CurrentPageCrop;
        width *= crop.Width;
        height *= crop.Width;
        var area = VisibleArea ?? crop;
        var x = Math.Clamp(area.ToPageX(0.5) - width / 2, crop.Left, Math.Max(crop.Left, crop.Right - width));
        var y = Math.Clamp(area.ToPageY(0.5) - height / 2, crop.Top, Math.Max(crop.Top, crop.Bottom - height));

        var annotation = await _annotationService.AddIconAsync(_sheet.Id, CurrentPageIndex, iconKey, x, y, width, height);

        CurrentPageAnnotations.Add(annotation);
        RecordAction(new AddAnnotationAction(_annotationService, CurrentPageAnnotations, annotation));
        SelectedAnnotation = annotation;
    }

    public async Task<Annotation> AddStrokeAsync(int sheetId, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)
    {
        var annotation = await _annotationService.AddStrokeAsync(sheetId, CurrentPageIndex, colorHex, strokeWidth, points);
        CurrentPageAnnotations.Add(annotation);
        RecordAction(new AddAnnotationAction(_annotationService, CurrentPageAnnotations, annotation));
        return annotation;
    }

    // Saves only the part of the stroke inside the page and the visible crop
    // (pieces of fewer than 2 points are dropped); undo removes them together.
    public async Task<IReadOnlyList<Annotation>> AddClippedStrokeAsync(int sheetId, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)
    {
        var pieces = StrokeClipper.Clip(points, CurrentPageCrop);
        var added = new List<Annotation>();
        var actions = new List<IUndoableAction>();
        foreach (var piece in pieces)
        {
            var annotation = await _annotationService.AddStrokeAsync(sheetId, CurrentPageIndex, colorHex, strokeWidth, piece);
            CurrentPageAnnotations.Add(annotation);
            actions.Add(new AddAnnotationAction(_annotationService, CurrentPageAnnotations, annotation));
            added.Add(annotation);
        }

        if (actions.Count == 1)
        {
            RecordAction(actions[0]);
        }
        else if (actions.Count > 1)
        {
            RecordAction(new CompositeUndoAction(actions));
        }

        return added;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var action = _undoStack.Pop();
        await action.UndoAsync();
        _redoStack.Push(action);
        _editSessionChanges = Math.Max(0, _editSessionChanges - 1);
        ClearSelectionIfNoLongerPresent();
        RefreshUndoRedoState();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var action = _redoStack.Pop();
        await action.RedoAsync();
        _undoStack.Push(action);
        _editSessionChanges++;
        ClearSelectionIfNoLongerPresent();
        RefreshUndoRedoState();
    }

    // Drops every page's history; called when a document is (re)opened.
    public void ClearUndoHistory()
    {
        _histories.Clear();
        _undoStack = new Stack<IUndoableAction>();
        _redoStack = new Stack<IUndoableAction>();
        _historyPageIndex = -1;
        _editSessionChanges = 0;
        RefreshUndoRedoState();
    }

    // Parks the current page's history (with the annotation instances it
    // refers to) and activates the one of the page being loaded.
    private void SwitchHistoryTo(int pageIndex)
    {
        // Also runs when reloading the same page: the reload creates new
        // annotation instances, so the old ones are still parked for reuse.
        if (_historyPageIndex >= 0)
        {
            if (_undoStack.Count > 0 || _redoStack.Count > 0)
            {
                // Merge: an overlapping reload may find the collection empty.
                var instances = _histories.TryGetValue(_historyPageIndex, out var parked)
                    ? parked.Instances
                    : new Dictionary<int, Annotation>();
                foreach (var annotation in CurrentPageAnnotations)
                {
                    instances[annotation.Id] = annotation;
                }

                _histories[_historyPageIndex] = new PageHistory
                {
                    Undo = _undoStack,
                    Redo = _redoStack,
                    Instances = instances
                };
            }
            else
            {
                _histories.Remove(_historyPageIndex);
            }
        }

        if (_historyPageIndex != pageIndex)
        {
            // The counter describes the page's stack; it doesn't carry over.
            _editSessionChanges = 0;
        }

        _historyPageIndex = pageIndex;
        if (_histories.TryGetValue(pageIndex, out var restored))
        {
            _undoStack = restored.Undo;
            _redoStack = restored.Redo;
        }
        else
        {
            _undoStack = new Stack<IUndoableAction>();
            _redoStack = new Stack<IUndoableAction>();
        }
    }

    private void RecordAction(IUndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        _editSessionChanges++;
        RefreshUndoRedoState();
    }

    private void RefreshUndoRedoState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void ClearSelectionIfNoLongerPresent()
    {
        if (SelectedAnnotation is not null && !CurrentPageAnnotations.Contains(SelectedAnnotation))
        {
            SelectedAnnotation = null;
        }
    }

    public async Task MoveSelectedAnnotationAsync(double oldX, double oldY, double newX, double newY)
    {
        var annotation = SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        annotation.X = newX;
        annotation.Y = newY;
        await _annotationService.UpdateAnnotationAsync(annotation);

        if (oldX != newX || oldY != newY)
        {
            RecordAction(new UpdateAnnotationAction(
                _annotationService,
                annotation,
                before: (oldX, oldY, annotation.Width, annotation.Height),
                after: (newX, newY, annotation.Width, annotation.Height)));
        }
    }

    public async Task ResizeSelectedAnnotationAsync(double oldWidth, double oldHeight, double newWidth, double newHeight, double? oldX = null)
    {
        var annotation = SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        annotation.Width = newWidth;
        annotation.Height = newHeight;
        await _annotationService.UpdateAnnotationAsync(annotation);

        var beforeX = oldX ?? annotation.X;
        if (oldWidth != newWidth || oldHeight != newHeight || beforeX != annotation.X)
        {
            RecordAction(new UpdateAnnotationAction(
                _annotationService,
                annotation,
                before: (beforeX, annotation.Y, oldWidth, oldHeight),
                after: (annotation.X, annotation.Y, newWidth, newHeight)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedAnnotation))]
    private async Task DeleteSelectedAnnotationAsync()
    {
        var annotation = SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        await _annotationService.DeleteAnnotationAsync(annotation);
        CurrentPageAnnotations.Remove(annotation);
        SelectedAnnotation = null;
        RecordAction(new DeleteAnnotationAction(_annotationService, CurrentPageAnnotations, annotation));
    }

    public async Task EraseAnnotationAsync(Annotation annotation)
    {
        await _annotationService.DeleteAnnotationAsync(annotation);
        CurrentPageAnnotations.Remove(annotation);
        if (SelectedAnnotation == annotation)
        {
            SelectedAnnotation = null;
        }

        var action = new DeleteAnnotationAction(_annotationService, CurrentPageAnnotations, annotation);
        if (_eraseBatch is not null)
        {
            _eraseBatch.Add(action);
        }
        else
        {
            RecordAction(action);
        }
    }

    public void BeginEraseBatch()
    {
        _eraseBatch = new List<IUndoableAction>();
    }

    public void EndEraseBatch()
    {
        var batch = _eraseBatch;
        _eraseBatch = null;

        if (batch is { Count: > 0 })
        {
            RecordAction(new CompositeUndoAction(batch));
        }
    }

    // Navigation updates CurrentPageIndex synchronously before calling
    // this, and further page turns can happen while it is awaiting - so
    // after every await it bails out if the user has already moved on,
    // leaving the newer call to populate the page.
    private async Task LoadCurrentPageAsync()
    {
        var pageIndex = CurrentPageIndex;
        _keepFirst = Math.Max(0, pageIndex - PrefetchRadius);
        _keepLast = pageIndex + PrefetchRadius;

        SelectedAnnotation = null;
        SwitchHistoryTo(pageIndex);
        CurrentPageAnnotations.Clear();
        RefreshUndoRedoState();

        var renderTask = GetPageImageAsync(pageIndex);
        if (IsShowingThumbnail && pageIndex != _thumbnailPageIndex)
        {
            // The user moved off the opening page: its thumbnail is stale.
            OpeningThumbnailBytes = null;
            IsShowingThumbnail = false;
        }

        if (!renderTask.IsCompleted && !IsShowingThumbnail)
        {
            // Not prefetched yet - blank the previous page rather than
            // leave it on screen under the new page number.
            CurrentPageImageBytes = null;
            IsLoading = true;
        }

        byte[]? imageBytes;
        try
        {
            imageBytes = await renderTask;
        }
        finally
        {
            if (pageIndex == CurrentPageIndex)
            {
                IsLoading = false;
            }
        }

        if (pageIndex != CurrentPageIndex)
        {
            return;
        }

        // The image is the cropped part; the aspect ratio annotations are
        // normalized against is the whole page's.
        var pageCrop = GetPageCrop(pageIndex);
        CurrentPageAspectRatio = imageBytes is not null && PngDimensions.TryRead(imageBytes, out var imageWidth, out var imageHeight)
            ? pageCrop.FullAspectRatio((double)imageHeight / imageWidth)
            : 0;
        CurrentPageCrop = pageCrop;
        CurrentPageImageBytes = imageBytes;
        if (pageIndex == _thumbnailPageIndex)
        {
            _thumbnailApplicable = false;
        }

        IsShowingThumbnail = false;
        SaveOpeningThumbnail(pageIndex, imageBytes);

        if (_sheet is not null)
        {
            var annotations = await LoadPageAnnotationsAsync(pageIndex, CurrentPageAspectRatio);
            if (pageIndex != CurrentPageIndex)
            {
                return;
            }

            var known = _histories.TryGetValue(pageIndex, out var history) ? history.Instances : null;
            foreach (var annotation in annotations)
            {
                // Reuse the instance the page's undo history refers to.
                CurrentPageAnnotations.Add(
                    known is not null && known.TryGetValue(annotation.Id, out var existing) ? existing : annotation);
            }
        }

        EvictDistantPages();
        PrefetchNeighbours(pageIndex);
    }

    private Task<byte[]?> GetPageImageAsync(int pageIndex)
    {
        if (!_pageRenders.TryGetValue(pageIndex, out var task) || task.IsFaulted || task.IsCanceled)
        {
            task = RenderPageGatedAsync(pageIndex);
            _pageRenders[pageIndex] = task;
        }

        return task;
    }

    // Reads a page's annotations in page coordinates, converting legacy ones
    // on the way. The conversion is saved only when both the stable
    // LegacyReferenceSize and the page aspect are known; otherwise the render
    // target stands in for display and the rows stay legacy in the database.
    private Task<List<Annotation>> LoadPageAnnotationsAsync(int pageIndex, double pageAspectRatio)
    {
        var targetAspect = _targetWidthPx > 0 ? (double)_targetHeightPx / _targetWidthPx : 0;
        var known = LegacyReferenceSize is { Width: > 0, Height: > 0 } && pageAspectRatio > 0;
        var (areaWidth, areaHeight) = LegacyReferenceSize is { Width: > 0, Height: > 0 }
            ? LegacyReferenceSize
            : (_targetWidthPx, _targetHeightPx);

        return _annotationService.GetPageAnnotationsAsync(
            _sheet!.Id, pageIndex, pageAspectRatio > 0 ? pageAspectRatio : targetAspect, areaWidth, areaHeight, persist: known);
    }

    private async Task<byte[]?> RenderPageGatedAsync(int pageIndex)
    {
        await _renderGate.WaitAsync();
        try
        {
            if (!IsInKeepWindow(pageIndex))
            {
                _pageRenders.Remove(pageIndex);
                return null;
            }

            var crop = GetPageCrop(pageIndex);
            return await _pdfPageRenderer.RenderPageAsync(
                _filePath, pageIndex, _targetWidthPx, _targetHeightPx, crop.IsFull ? null : crop);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    // Nearest pages first, forward before backward - the next page is the
    // one a reader most likely turns to.
    private void PrefetchNeighbours(int pageIndex)
    {
        for (var distance = 1; distance <= PrefetchRadius; distance++)
        {
            foreach (var neighbour in new[] { pageIndex + distance, pageIndex - distance })
            {
                if (neighbour >= 0 && neighbour < PageCount)
                {
                    _ = GetPageImageAsync(neighbour);
                }
            }
        }
    }

    private bool IsInKeepWindow(int pageIndex) => pageIndex >= _keepFirst && pageIndex <= _keepLast;

    private void EvictDistantPages()
    {
        var distant = _pageRenders
            .Where(entry => !IsInKeepWindow(entry.Key) && entry.Value.IsCompleted)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in distant)
        {
            _pageRenders.Remove(key);
        }
    }

    // Renders the visible part of a page at the resolution it has on screen
    // (the region is normalized to the page as displayed, i.e. cropped).
    // Cancellable: a newer pan/zoom/page change supersedes it.
    public async Task<byte[]?> RenderPageDetailAsync(
        int pageIndex, DetailRegion region, CancellationToken cancellationToken)
    {
        if (_sheet is null || pageIndex < 0 || pageIndex >= PageCount)
        {
            return null;
        }

        await _detailGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var crop = GetPageCrop(pageIndex);
            return await _pdfPageRenderer.RenderPageRegionAsync(
                _filePath, pageIndex, region.NormX, region.NormY, region.NormWidth, region.NormHeight,
                region.PixelWidth, region.PixelHeight, crop.IsFull ? null : crop, cancellationToken);
        }
        finally
        {
            _detailGate.Release();
        }
    }

    // The page rendered whole (the crop screen shows it to crop from).
    public async Task<byte[]?> RenderUncroppedPageAsync(int pageIndex)
    {
        if (_sheet is null || pageIndex < 0 || pageIndex >= PageCount)
        {
            return null;
        }

        await _renderGate.WaitAsync();
        try
        {
            return await _pdfPageRenderer.RenderPageAsync(_filePath, pageIndex, _targetWidthPx, _targetHeightPx);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    // Saves (or, for a full-page crop, clears) a page's crop and re-renders
    // the page with it. Cached renders of the page are dropped - they are
    // keyed by page only, so they would still show the old crop.
    public async Task SetPageCropAsync(int pageIndex, PageCrop crop)
    {
        if (_sheet is null || pageIndex < 0 || pageIndex >= PageCount)
        {
            return;
        }

        crop = crop.OrFull();
        await _pageCropService.SetCropAsync(_sheet.Id, pageIndex, crop);

        if (crop.IsFull)
        {
            _crops.Remove(pageIndex);
        }
        else
        {
            _crops[pageIndex] = crop;
        }

        _pageRenders.Remove(pageIndex);

        if (IsContinuousReading && !IsContinuousEditing)
        {
            var page = ContinuousPages[pageIndex];
            var fullAspect = page.FullAspectRatio;
            page.IsLoadRequested = false;
            page.ImageBytes = null;
            page.Annotations = Array.Empty<Annotation>();
            // Keep the row's height plausible until the new image arrives.
            page.Crop = crop;
            page.AspectRatio = crop.CroppedAspectRatio(fullAspect);
            await UpdateContinuousViewportAsync(
                Math.Min(_keepFirst + 1, CurrentPageIndex), Math.Max(_keepLast - 1, CurrentPageIndex), CurrentPageIndex);
        }
        else
        {
            await LoadCurrentPageAsync();
        }
    }

    // Stores the thumbnail of the page the viewer opened on, once, after its
    // full render. Off the UI thread; failures are ignored by the store.
    private void SaveOpeningThumbnail(int pageIndex, byte[]? imageBytes)
    {
        if (!_saveThumbnailAfterRender || _thumbnails is null || _sheet is null || imageBytes is null || pageIndex != CurrentPageIndex)
        {
            return;
        }

        _saveThumbnailAfterRender = false;
        _ = _thumbnails.SaveAsync(_sheet.Id, pageIndex, imageBytes);
    }

    private async Task ApplyOpeningThumbnailAsync(int sheetId, int pageIndex, int openId)
    {
        var thumbnail = await LoadThumbnailAsync(sheetId, pageIndex);
        if (thumbnail is null
            || !_thumbnailApplicable
            || openId != _openVersion
            || pageIndex != CurrentPageIndex
            || _sheet?.Id != sheetId)
        {
            return;
        }

        if (IsContinuousReading)
        {
            if (pageIndex >= ContinuousPages.Count)
            {
                return;
            }

            var openingPage = ContinuousPages[pageIndex];
            if (openingPage.ImageBytes is not null)
            {
                return;
            }

            if (PngDimensions.TryRead(thumbnail, out var thumbWidth, out var thumbHeight))
            {
                openingPage.AspectRatio = (double)thumbHeight / thumbWidth;
            }

            openingPage.ImageBytes = thumbnail;
            IsShowingThumbnail = true;
        }
        else
        {
            if (CurrentPageImageBytes is not null)
            {
                return;
            }

            CurrentPageImageBytes = thumbnail;
            OpeningThumbnailBytes = thumbnail;
            IsShowingThumbnail = true;
        }
    }

    private async Task<byte[]?> LoadThumbnailAsync(int sheetId, int pageIndex)
    {
        try
        {
            return _thumbnails is null ? null : await _thumbnails.TryLoadAsync(sheetId, pageIndex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail load failed: {ex.Message}");
            return null;
        }
    }

    // Called when leaving the viewer so the next opening shows the page the
    // user left on.
    public void SaveCurrentPageThumbnail()
    {
        if (_thumbnails is null || _sheet is null || IsShowingThumbnail || PageCount <= 0)
        {
            return;
        }

        var bytes = IsContinuousReading
            ? (CurrentPageIndex < ContinuousPages.Count ? ContinuousPages[CurrentPageIndex].ImageBytes : null)
            : CurrentPageImageBytes;
        if (bytes is not null)
        {
            _ = _thumbnails.SaveAsync(_sheet.Id, CurrentPageIndex, bytes);
        }
    }

    private async Task PersistLastViewedPageAsync()
    {
        if (_sheet is not null)
        {
            await _libraryService.UpdateSheetLastViewedPageAsync(_sheet, CurrentPageIndex);
        }
    }
}
