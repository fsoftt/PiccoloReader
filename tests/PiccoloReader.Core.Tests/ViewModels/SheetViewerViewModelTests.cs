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
    private readonly AnnotationService _annotationService;
    private readonly BookmarkService _bookmarkService;
    private readonly SheetViewerViewModel _sut;

    public SheetViewerViewModelTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(_database, _storage);
        _annotationService = new AnnotationService(_database);
        _bookmarkService = new BookmarkService(_database);
        _sut = new SheetViewerViewModel(_libraryService, _storage, _renderer, _annotationService, _bookmarkService);
    }

    public void Dispose() => _storage.Dispose();

    private async Task<Sheet> InsertSheetAsync(int pageCount, int lastViewedPageIndex = 0)
    {
        var sheet = new Sheet
        {
            FolderId = null,
            Title = "My Piece",
            FileName = "irrelevant.pdf",
            PageCount = pageCount,
            LastViewedPageIndex = lastViewedPageIndex,
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

    [Fact]
    public async Task LoadAsync_ResumesFromLastViewedPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5, lastViewedPageIndex: 3);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(3, _sut.CurrentPageIndex);
        Assert.Equal(new byte[] { 3 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task LoadAsync_LastViewedPageOutOfRange_ClampsToLastPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5, lastViewedPageIndex: 99);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(4, _sut.CurrentPageIndex);
    }

    [Fact]
    public async Task NextPageAsync_PersistsLastViewedPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.NextPageCommand.ExecuteAsync(null);

        var reloaded = await _libraryService.GetSheetAsync(sheet.Id);
        Assert.Equal(1, reloaded.LastViewedPageIndex);
    }

    [Fact]
    public async Task PreviousPageAsync_PersistsLastViewedPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5, lastViewedPageIndex: 2);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.PreviousPageCommand.ExecuteAsync(null);

        var reloaded = await _libraryService.GetSheetAsync(sheet.Id);
        Assert.Equal(1, reloaded.LastViewedPageIndex);
    }

    [Fact]
    public async Task LoadAsync_LoadsAnnotationsForCurrentPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _annotationService.AddIconAsync(sheet.Id, pageIndex: 0, "dynamicForte", 0.4, 0.5, 0.1, 0.09);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Single(_sut.CurrentPageAnnotations);
        Assert.Equal("dynamicForte", _sut.CurrentPageAnnotations[0].IconKey);
    }

    [Fact]
    public async Task NextPageAsync_ReloadsAnnotationsForNewPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _annotationService.AddIconAsync(sheet.Id, pageIndex: 1, "dynamicPiano", 0.3, 0.3, 0.1, 0.1);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        Assert.Empty(_sut.CurrentPageAnnotations);

        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.Single(_sut.CurrentPageAnnotations);
        Assert.Equal("dynamicPiano", _sut.CurrentPageAnnotations[0].IconKey);
    }

    [Fact]
    public async Task PlaceIconAsync_AddsAnnotationCenteredWithCatalogAspectRatio()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        Assert.Single(_sut.CurrentPageAnnotations);
        var placed = _sut.CurrentPageAnnotations[0];
        Assert.Equal("dynamicForte", placed.IconKey);
        Assert.Equal(0.12, placed.Width, precision: 6);
        Assert.Equal(0.12 / 0.85, placed.Height, precision: 6);
        Assert.Equal(0.5 - placed.Width / 2, placed.X, precision: 6);
        Assert.Equal(0.5 - placed.Height / 2, placed.Y, precision: 6);

        var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
        Assert.Single(persisted);
    }

    [Fact]
    public async Task PlaceIconAsync_SelectsTheNewIcon()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        Assert.NotNull(_sut.SelectedAnnotation);
        Assert.Equal("dynamicForte", _sut.SelectedAnnotation!.IconKey);
    }

    [Fact]
    public async Task MoveSelectedAnnotationAsync_UpdatesAndPersistsPosition()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        await _sut.MoveSelectedAnnotationAsync(0.2, 0.3);

        Assert.Equal(0.2, _sut.SelectedAnnotation!.X);
        Assert.Equal(0.3, _sut.SelectedAnnotation!.Y);
        var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
        Assert.Equal(0.2, persisted[0].X);
    }

    [Fact]
    public async Task ResizeSelectedAnnotationAsync_UpdatesAndPersistsSize()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        await _sut.ResizeSelectedAnnotationAsync(0.2, 0.18);

        Assert.Equal(0.2, _sut.SelectedAnnotation!.Width);
        Assert.Equal(0.18, _sut.SelectedAnnotation!.Height);
        var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
        Assert.Equal(0.2, persisted[0].Width);
    }

    [Fact]
    public async Task DeleteSelectedAnnotationAsync_RemovesFromCollectionAndPersistence()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        await _sut.DeleteSelectedAnnotationCommand.ExecuteAsync(null);

        Assert.Empty(_sut.CurrentPageAnnotations);
        Assert.Null(_sut.SelectedAnnotation);
        var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
        Assert.Empty(persisted);
    }

    [Fact]
    public async Task ActiveTool_DefaultsToMusicIcons()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(AnnotationTool.MusicIcons, _sut.ActiveTool);
        Assert.False(_sut.IsDrawingToolActive);
    }

    [Fact]
    public void IsDrawingToolActive_TrueWhenPencilOrEraserActive()
    {
        _sut.ActiveTool = AnnotationTool.Pencil;
        Assert.True(_sut.IsDrawingToolActive);

        _sut.ActiveTool = AnnotationTool.Eraser;
        Assert.True(_sut.IsDrawingToolActive);

        _sut.ActiveTool = AnnotationTool.MusicIcons;
        Assert.False(_sut.IsDrawingToolActive);
    }

    [Fact]
    public async Task GoToPageAsync_JumpsToPageAndRendersIt()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.GoToPageAsync(3);

        Assert.Equal(3, _sut.CurrentPageIndex);
        Assert.Equal(new byte[] { 3 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task GoToPageAsync_PersistsLastViewedPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.GoToPageAsync(3);

        var reloaded = await _libraryService.GetSheetAsync(sheet.Id);
        Assert.Equal(3, reloaded.LastViewedPageIndex);
    }

    [Fact]
    public async Task GoToPageAsync_OutOfRange_DoesNothing()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.GoToPageAsync(10);

        Assert.Equal(0, _sut.CurrentPageIndex);
    }

    [Fact]
    public async Task AddBookmarkAsync_AddsToCollectionAndPersists()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.AddBookmarkAsync(2);

        Assert.Single(_sut.Bookmarks);
        Assert.Equal(2, _sut.Bookmarks[0].PageIndex);
        Assert.Null(_sut.Bookmarks[0].Name);
        var persisted = await _bookmarkService.GetBookmarksAsync(sheet.Id);
        Assert.Single(persisted);
    }

    [Fact]
    public async Task AddBookmarkAsync_WithName_AddsToCollectionAndPersists()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.AddBookmarkAsync(2, "Coda");

        Assert.Single(_sut.Bookmarks);
        Assert.Equal("Coda", _sut.Bookmarks[0].Name);
        var persisted = await _bookmarkService.GetBookmarksAsync(sheet.Id);
        Assert.Equal("Coda", persisted[0].Name);
    }

    [Fact]
    public async Task LoadAsync_LoadsExistingBookmarksForSheet()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _bookmarkService.AddBookmarkAsync(sheet.Id, 1);
        await _bookmarkService.AddBookmarkAsync(sheet.Id, 4);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(2, _sut.Bookmarks.Count);
    }

    [Fact]
    public async Task UndoCommand_InitiallyCannotExecute()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.False(_sut.CanUndo);
        Assert.False(_sut.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task UndoCommand_AfterPlaceIcon_RemovesTheIconAndEnablesRedo()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        await _sut.UndoCommand.ExecuteAsync(null);

        Assert.Empty(_sut.CurrentPageAnnotations);
        Assert.Empty(await _annotationService.GetAnnotationsAsync(sheet.Id, 0));
        Assert.False(_sut.CanUndo);
        Assert.True(_sut.CanRedo);
    }

    [Fact]
    public async Task RedoCommand_AfterUndoingPlaceIcon_RestoresTheIcon()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");
        await _sut.UndoCommand.ExecuteAsync(null);

        await _sut.RedoCommand.ExecuteAsync(null);

        Assert.Single(_sut.CurrentPageAnnotations);
        Assert.Equal("dynamicForte", _sut.CurrentPageAnnotations[0].IconKey);
        Assert.True(_sut.CanUndo);
        Assert.False(_sut.CanRedo);
    }

    [Fact]
    public async Task PlaceIconCommand_AfterUndo_NewActionClearsRedoStack()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");
        await _sut.UndoCommand.ExecuteAsync(null);

        await _sut.PlaceIconCommand.ExecuteAsync("dynamicPiano");

        Assert.False(_sut.CanRedo);
    }

    [Fact]
    public async Task UndoCommand_AfterAddStroke_RemovesTheStroke()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        var points = new List<StrokePoint> { new(0.1, 0.1), new(0.2, 0.2) };
        await _sut.AddStrokeAsync(sheet.Id, "#000000", 0.01, points);

        await _sut.UndoCommand.ExecuteAsync(null);

        Assert.Empty(_sut.CurrentPageAnnotations);
    }

    [Fact]
    public async Task NextPageCommand_ResetsUndoRedoStacks()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.False(_sut.CanUndo);
        Assert.False(_sut.CanRedo);
    }

    [Fact]
    public async Task UndoCommand_AfterDeleteSelectedAnnotation_RestoresIt()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");
        await _sut.DeleteSelectedAnnotationCommand.ExecuteAsync(null);

        await _sut.UndoCommand.ExecuteAsync(null);

        Assert.Single(_sut.CurrentPageAnnotations);
        Assert.Equal("dynamicForte", _sut.CurrentPageAnnotations[0].IconKey);
    }

    [Fact]
    public async Task UndoCommand_AfterEraseAnnotation_RestoresIt()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");
        var annotation = _sut.CurrentPageAnnotations[0];

        await _sut.EraseAnnotationAsync(annotation);
        await _sut.UndoCommand.ExecuteAsync(null);

        Assert.Single(_sut.CurrentPageAnnotations);
    }

    [Fact]
    public async Task EraseBatch_MultipleErasuresInOneBatch_UndoRestoresAllInOneStep()
    {
        var sheet = await InsertSheetAsync(pageCount: 3);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");
        var first = _sut.CurrentPageAnnotations[0];
        await _sut.PlaceIconCommand.ExecuteAsync("dynamicPiano");
        var second = _sut.CurrentPageAnnotations[1];

        _sut.BeginEraseBatch();
        await _sut.EraseAnnotationAsync(first);
        await _sut.EraseAnnotationAsync(second);
        _sut.EndEraseBatch();

        Assert.Empty(_sut.CurrentPageAnnotations);
        Assert.True(_sut.CanUndo);

        await _sut.UndoCommand.ExecuteAsync(null);

        // One undo restores both erased icons at once - the stack still has
        // the two earlier PlaceIconCommand actions underneath, so CanUndo
        // stays true; it's the count restored in a single step that matters.
        Assert.Equal(2, _sut.CurrentPageAnnotations.Count);
        Assert.True(_sut.CanUndo);
    }

    [Fact]
    public void EndEraseBatch_WithNoErasures_DoesNotPushAnAction()
    {
        _sut.BeginEraseBatch();
        _sut.EndEraseBatch();

        Assert.False(_sut.CanUndo);
    }

    [Fact]
    public void PageIndicatorText_SpanishCulture_UsesSpanishFormat()
    {
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("es");
            Assert.Contains("Página", _sut.PageIndicatorText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }
}
