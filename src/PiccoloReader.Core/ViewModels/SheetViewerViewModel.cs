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

    private readonly LibraryService _libraryService;
    private readonly IAppStorageProvider _storageProvider;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly AnnotationService _annotationService;
    private readonly BookmarkService _bookmarkService;
    private readonly IAdsPreferenceService _adsPreferenceService;

    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private List<IUndoableAction>? _eraseBatch;

    private Sheet? _sheet;
    private string _filePath = string.Empty;
    private int _targetWidthPx;
    private int _targetHeightPx;

    public SheetViewerViewModel(
        LibraryService libraryService,
        IAppStorageProvider storageProvider,
        IPdfPageRenderer pdfPageRenderer,
        AnnotationService annotationService,
        BookmarkService bookmarkService,
        IAdsPreferenceService adsPreferenceService)
    {
        _libraryService = libraryService;
        _storageProvider = storageProvider;
        _pdfPageRenderer = pdfPageRenderer;
        _annotationService = annotationService;
        _bookmarkService = bookmarkService;
        _adsPreferenceService = adsPreferenceService;
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
    private bool _isLoading;

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

    public bool IsDrawingToolActive => ActiveTool != AnnotationTool.MusicIcons;

    public ObservableCollection<Annotation> CurrentPageAnnotations { get; } = new();

    public ObservableCollection<Bookmark> Bookmarks { get; } = new();

    public int CurrentPageDisplay => CurrentPageIndex + 1;

    public string PageIndicatorText => string.Format(AppStrings.PageIndicatorFormat, CurrentPageDisplay, PageCount);

    public async Task LoadAsync(int sheetId, int targetWidthPx, int targetHeightPx)
    {
        ShowAdsBanner = _adsPreferenceService.GetSupportWithAdsEnabled();

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

            Bookmarks.Clear();
            foreach (var bookmark in await _bookmarkService.GetBookmarksAsync(sheetId))
            {
                Bookmarks.Add(bookmark);
            }

            await LoadCurrentPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task GoToPageAsync(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount || pageIndex == CurrentPageIndex)
        {
            return;
        }

        CurrentPageIndex = pageIndex;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
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

    private bool CanGoToNextPage() => CurrentPageIndex < PageCount - 1;

    private bool CanGoToPreviousPage() => CurrentPageIndex > 0;

    private bool CanDeleteSelectedAnnotation() => SelectedAnnotation is not null;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        CurrentPageIndex++;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task PreviousPageAsync()
    {
        CurrentPageIndex--;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

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
        var height = width / aspectRatio;
        var x = 0.5 - width / 2;
        var y = 0.5 - height / 2;

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
        ClearSelectionIfNoLongerPresent();
        RefreshUndoRedoState();
    }

    private void RecordAction(IUndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
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

    public async Task ResizeSelectedAnnotationAsync(double oldWidth, double oldHeight, double newWidth, double newHeight)
    {
        var annotation = SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        annotation.Width = newWidth;
        annotation.Height = newHeight;
        await _annotationService.UpdateAnnotationAsync(annotation);

        if (oldWidth != newWidth || oldHeight != newHeight)
        {
            RecordAction(new UpdateAnnotationAction(
                _annotationService,
                annotation,
                before: (annotation.X, annotation.Y, oldWidth, oldHeight),
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

    private async Task LoadCurrentPageAsync()
    {
        CurrentPageImageBytes = await _pdfPageRenderer.RenderPageAsync(_filePath, CurrentPageIndex, _targetWidthPx, _targetHeightPx);

        SelectedAnnotation = null;
        CurrentPageAnnotations.Clear();
        if (_sheet is not null)
        {
            var annotations = await _annotationService.GetAnnotationsAsync(_sheet.Id, CurrentPageIndex);
            foreach (var annotation in annotations)
            {
                CurrentPageAnnotations.Add(annotation);
            }
        }

        _undoStack.Clear();
        _redoStack.Clear();
        RefreshUndoRedoState();
    }

    private async Task PersistLastViewedPageAsync()
    {
        if (_sheet is not null)
        {
            await _libraryService.UpdateSheetLastViewedPageAsync(_sheet, CurrentPageIndex);
        }
    }
}
