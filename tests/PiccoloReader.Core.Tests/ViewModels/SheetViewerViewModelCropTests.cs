using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SheetViewerViewModelCropTests : IDisposable
{
    private static readonly PageCrop Crop = new(0.1, 0.2, 0.7, 0.8);

    private readonly TestAppStorageProvider _storage = new();
    private readonly AppDatabase _database;
    private readonly FakePdfPageRenderer _renderer = new();
    private readonly FakeReadingPreferenceService _readingPreferences = new();
    private readonly PageCropService _cropService;
    private readonly SheetViewerViewModel _sut;

    public SheetViewerViewModelCropTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _cropService = new PageCropService(_database);
        _sut = CreateViewModel();
    }

    public void Dispose() => _storage.Dispose();

    private SheetViewerViewModel CreateViewModel() => new(
        new LibraryService(_database, _storage),
        _storage,
        _renderer,
        new AnnotationService(_database),
        new BookmarkService(_database),
        new FakeAdsPreferenceService(),
        _readingPreferences,
        _cropService);

    private async Task<Sheet> InsertSheetAsync(int pageCount)
    {
        var sheet = new Sheet
        {
            Title = "My Piece",
            FileName = "irrelevant.pdf",
            PageCount = pageCount,
            DateAdded = DateTime.UtcNow
        };
        await _database.Connection.InsertAsync(sheet);
        return sheet;
    }

    [Fact]
    public async Task SetPageCrop_PersistsAndIsLoadedByANewViewModel()
    {
        var sheet = await InsertSheetAsync(3);
        await _sut.LoadAsync(sheet.Id, 800, 1000);

        await _sut.SetPageCropAsync(0, Crop);

        var reopened = CreateViewModel();
        await reopened.LoadAsync(sheet.Id, 800, 1000);
        Assert.Equal(Crop, reopened.GetPageCrop(0));
        Assert.Equal(Crop, reopened.CurrentPageCrop);
        Assert.True(reopened.GetPageCrop(1).IsFull);
    }

    [Fact]
    public async Task SetPageCrop_ReRendersThePageWithTheCrop()
    {
        var sheet = await InsertSheetAsync(3);
        await _sut.LoadAsync(sheet.Id, 800, 1000);
        _renderer.RenderedPageIndexes.Clear();
        _renderer.RenderedCrops.Clear();

        await _sut.SetPageCropAsync(0, Crop);

        var index = _renderer.RenderedPageIndexes.IndexOf(0);
        Assert.True(index >= 0);
        Assert.Equal(Crop, _renderer.RenderedCrops[index]);
        Assert.Equal(Crop, _sut.CurrentPageCrop);
    }

    [Fact]
    public async Task OtherPages_AreRenderedWithoutACrop()
    {
        var sheet = await InsertSheetAsync(3);
        await _cropService.SetCropAsync(sheet.Id, 1, Crop);
        _renderer.RenderedPageIndexes.Clear();
        _renderer.RenderedCrops.Clear();

        await _sut.LoadAsync(sheet.Id, 800, 1000);

        var page0 = _renderer.RenderedPageIndexes.IndexOf(0);
        var page1 = _renderer.RenderedPageIndexes.IndexOf(1);
        Assert.Null(_renderer.RenderedCrops[page0]);
        Assert.Equal(Crop, _renderer.RenderedCrops[page1]);
        Assert.True(_sut.CurrentPageCrop.IsFull);
    }

    [Fact]
    public async Task SetPageCrop_Full_ResetsThePage()
    {
        var sheet = await InsertSheetAsync(2);
        await _sut.LoadAsync(sheet.Id, 800, 1000);
        await _sut.SetPageCropAsync(0, Crop);

        await _sut.SetPageCropAsync(0, PageCrop.Full);

        Assert.True(_sut.GetPageCrop(0).IsFull);
        Assert.True(_sut.CurrentPageCrop.IsFull);
        Assert.Empty(await _cropService.GetCropsAsync(sheet.Id));
    }

    [Fact]
    public async Task SetPageCrop_ContinuousMode_UpdatesTheListPage()
    {
        _readingPreferences.Mode = ReadingMode.VerticalContinuous;
        var sheet = await InsertSheetAsync(4);
        await _sut.LoadAsync(sheet.Id, 800, 1000);
        var before = _sut.ContinuousPages[0].AspectRatio;

        await _sut.SetPageCropAsync(0, Crop);

        var page = _sut.ContinuousPages[0];
        Assert.Equal(Crop, page.Crop);
        Assert.Equal(Crop.CroppedAspectRatio(before), page.AspectRatio, 9);
        Assert.NotNull(page.ImageBytes);
        Assert.True(_sut.ContinuousPages[1].Crop.IsFull);
    }

    [Fact]
    public async Task LoadAsync_ContinuousPagesStartWithTheirCropAspect()
    {
        var sheet = await InsertSheetAsync(3);
        await _cropService.SetCropAsync(sheet.Id, 1, new PageCrop(0, 0, 0.5, 1));

        await _sut.LoadAsync(sheet.Id, 800, 1000);

        Assert.Equal(1.25, _sut.ContinuousPages[0].AspectRatio, 9);
        Assert.Equal(2.5, _sut.ContinuousPages[1].AspectRatio, 9);
        Assert.Equal(1.25, _sut.ContinuousPages[1].FullAspectRatio, 9);
    }

    [Fact]
    public async Task PlaceIcon_OnCroppedPage_IsCenteredInTheVisiblePart()
    {
        var sheet = await InsertSheetAsync(2);
        await _cropService.SetCropAsync(sheet.Id, 0, Crop);
        await _sut.LoadAsync(sheet.Id, 800, 1000);

        await _sut.PlaceIconCommand.ExecuteAsync("forte");

        var annotation = Assert.Single(_sut.CurrentPageAnnotations);
        Assert.Equal(Crop.ToPageX(0.5), annotation.X + annotation.Width / 2, 9);
        Assert.Equal(Crop.ToPageY(0.5), annotation.Y + annotation.Height / 2, 9);
        Assert.True(Crop.Contains(annotation.X, annotation.Y));
    }

    [Fact]
    public async Task Annotations_StayInFullPageCoordinatesWhenCropChanges()
    {
        var sheet = await InsertSheetAsync(2);
        var annotationService = new AnnotationService(_database);
        await annotationService.AddIconAsync(sheet.Id, 0, "forte", 0.3, 0.4, 0.1, 0.1);
        await _sut.LoadAsync(sheet.Id, 800, 1000);

        await _sut.SetPageCropAsync(0, Crop);
        var stored = await _database.Connection.Table<Annotation>().FirstAsync();

        Assert.Equal(0.3, stored.X, 9);
        Assert.Equal(0.4, stored.Y, 9);
        Assert.Equal(0.3, _sut.CurrentPageAnnotations.Single().X, 9);
    }
}
