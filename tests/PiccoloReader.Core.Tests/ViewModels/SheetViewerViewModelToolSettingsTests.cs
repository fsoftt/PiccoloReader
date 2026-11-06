using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SheetViewerViewModelToolSettingsTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AppDatabase _database;
    private readonly FakeAnnotationToolPreferenceService _prefs = new();
    private readonly ManualTimeProvider _time = new();

    public SheetViewerViewModelToolSettingsTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _storage.Dispose();

    private SheetViewerViewModel Create() =>
        new(new LibraryService(_database, _storage), _storage, new FakePdfPageRenderer(), new AnnotationService(_database),
            new BookmarkService(_database), new FakeReadingPreferenceService(), new PageCropService(_database),
            null, _time, null, null, _prefs);

    private async Task SettleAsync()
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(100);
    }

    [Fact]
    public void NothingSaved_UsesDefaults()
    {
        var sut = Create();

        Assert.Equal(AnnotationToolSettings.MinPencilStrokeWidth, sut.PencilStrokeWidth);
        Assert.Equal("#000000", sut.PencilColorHex);
        Assert.Equal(HighlighterPalette.Default, sut.HighlighterColorHex);
        Assert.Equal(HighlighterPalette.DefaultStrokeWidth, sut.HighlighterStrokeWidth);
        Assert.Equal(0.05, sut.EraserRadius);
        Assert.Equal(AnnotationTool.MusicIcons, sut.ActiveTool);
    }

    [Fact]
    public void SavedValues_AreRestoredOnNewViewModel()
    {
        _prefs.Stored = new AnnotationToolSettings
        {
            PencilColorHex = "#FF0000",
            PencilStrokeWidth = 0.012,
            HighlighterColorHex = "#6676FF03",
            HighlighterStrokeWidth = 0.04,
            EraserRadius = 0.07,
            TextColorHex = "#0000FF",
        };

        var sut = Create();

        Assert.Equal("#FF0000", sut.PencilColorHex);
        Assert.Equal(0.012, sut.PencilStrokeWidth);
        Assert.Equal("#6676FF03", sut.HighlighterColorHex);
        Assert.Equal(0.04, sut.HighlighterStrokeWidth);
        Assert.Equal(0.07, sut.EraserRadius);
        Assert.Equal("#0000FF", sut.TextColorHex);
        Assert.Equal(AnnotationTool.MusicIcons, sut.ActiveTool);
        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public void InvalidSavedValues_AreClampedOrReplacedByDefaults()
    {
        _prefs.Stored = new AnnotationToolSettings
        {
            PencilColorHex = "not a color",
            PencilStrokeWidth = 5,
            HighlighterColorHex = "",
            HighlighterStrokeWidth = -1,
            EraserRadius = double.NaN,
            TextColorHex = null!,
        };

        var sut = Create();

        Assert.Equal("#000000", sut.PencilColorHex);
        Assert.Equal(AnnotationToolSettings.MaxPencilStrokeWidth, sut.PencilStrokeWidth);
        Assert.Equal(HighlighterPalette.Default, sut.HighlighterColorHex);
        Assert.Equal(HighlighterPalette.MinStrokeWidth, sut.HighlighterStrokeWidth);
        Assert.Equal(AnnotationToolSettings.DefaultEraserRadius, sut.EraserRadius);
        Assert.Equal("#000000", sut.TextColorHex);
    }

    [Fact]
    public async Task ChangingValue_SavesAfterItSettles()
    {
        var sut = Create();

        sut.PencilStrokeWidth = 0.004;
        sut.PencilColorHex = "#008000";
        Assert.Equal(0, _prefs.SaveCount);
        await SettleAsync();

        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(0.004, _prefs.Stored.PencilStrokeWidth);
        Assert.Equal("#008000", _prefs.Stored.PencilColorHex);
    }

    [Fact]
    public async Task Flush_SavesPendingChangeWithoutWaitingForDebounce()
    {
        var sut = Create();

        sut.PencilStrokeWidth = 0.004;
        sut.FlushToolSettingsSave();
        await Task.Delay(200);

        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(0.004, _prefs.Stored.PencilStrokeWidth);

        await SettleAsync();
        Assert.Equal(1, _prefs.SaveCount);
    }

    [Fact]
    public async Task Flush_WithNothingPending_DoesNotSave()
    {
        var sut = Create();

        sut.FlushToolSettingsSave();
        await Task.Delay(100);

        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public async Task DraggingSlider_SavesOnlyTheFinalValue()
    {
        var sut = Create();

        for (var i = 1; i <= 10; i++)
        {
            sut.HighlighterStrokeWidth = 0.01 + i * 0.002;
            _time.Advance(TimeSpan.FromMilliseconds(50));
        }

        await SettleAsync();

        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(0.03, _prefs.Stored.HighlighterStrokeWidth, 6);
    }

    [Fact]
    public async Task SavedChange_IsSeenByNextViewModel()
    {
        var first = Create();
        first.EraserRadius = 0.03;
        first.TextColorHex = "#FFA500";
        await SettleAsync();

        var second = Create();

        Assert.Equal(0.03, second.EraserRadius);
        Assert.Equal("#FFA500", second.TextColorHex);
    }
}
