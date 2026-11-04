using System.Collections.ObjectModel;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class TextFigureTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _annotationService;

    public TextFigureTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _annotationService = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Normalize_EmptyInput_IsNull(string? input) => Assert.Null(TextFigure.Normalize(input));

    [Fact]
    public void Normalize_TrimsAndJoinsLines()
    {
        Assert.Equal("dolce e piano", TextFigure.Normalize("  dolce\ne piano \n"));
    }

    [Fact]
    public void Normalize_Truncation_DoesNotSplitEmojiOrSurrogates()
    {
        var smile = "\U0001F600";
        var result = TextFigure.Normalize(new string('a', TextFigure.MaxLength - 1) + smile + smile)!;

        Assert.Equal(new string('a', TextFigure.MaxLength - 1) + smile, result);
    }

    [Fact]
    public void Normalize_CapsLength()
    {
        Assert.Equal(TextFigure.MaxLength, TextFigure.Normalize(new string('a', 500))!.Length);
    }

    [Fact]
    public void Size_WidthFollowsTextAspectInPageWidths()
    {
        var (width, height) = TextFigure.Size(textAspect: 5, TextFigure.DefaultHeight, pageAspectRatio: 1.4);

        Assert.Equal(TextFigure.DefaultHeight, height, 6);
        Assert.Equal(TextFigure.DefaultHeight * 1.4 * 5, width, 6);
    }

    [Fact]
    public void Size_TextWiderThanPage_ShrinksToFit()
    {
        var (width, height) = TextFigure.Size(textAspect: 100, TextFigure.DefaultHeight, pageAspectRatio: 1.4);

        Assert.Equal(1.0, width, 6);
        Assert.True(height < TextFigure.DefaultHeight);
    }

    [Fact]
    public void Place_TopLeftAtTap_WhenItFits()
    {
        var (x, y) = TextFigure.Place(0.3, 0.4, 0.2, 0.03, PageCrop.Full);

        Assert.Equal(0.3, x, 6);
        Assert.Equal(0.4, y, 6);
    }

    [Fact]
    public void Place_NearEdges_IsClampedInsideCrop()
    {
        var (x, y) = TextFigure.Place(0.95, 0.99, 0.2, 0.03, new PageCrop(0.1, 0.1, 0.9, 0.9));

        Assert.Equal(0.7, x, 6);
        Assert.Equal(0.87, y, 6);
    }

    [Fact]
    public async Task AddTextAsync_PersistsTypeAndText()
    {
        var annotation = await _annotationService.AddTextAsync(1, 2, "rit.", "#123456", 0.1, 0.2, 0.1, 0.025);

        var stored = (await _annotationService.GetAnnotationsAsync(1, 2)).Single();
        Assert.Equal(annotation.Id, stored.Id);
        Assert.True(stored.IsText);
        Assert.False(stored.IsStroke);
        Assert.Equal("rit.", stored.Text);
        Assert.Equal("#123456", stored.ColorHex);
    }

    [Fact]
    public async Task UpdateAction_WithText_UndoRestoresTextAndBox_RedoReapplies()
    {
        var annotation = await _annotationService.AddTextAsync(1, 0, "old", "#000000", 0.1, 0.2, 0.1, 0.025);
        var before = (annotation.X, annotation.Y, annotation.Width, annotation.Height);
        annotation.Text = "a longer text";
        annotation.Width = 0.3;
        await _annotationService.UpdateAnnotationAsync(annotation);
        var sut = new UpdateAnnotationAction(_annotationService, annotation, before, (0.1, 0.2, 0.3, 0.025), "old", "a longer text");

        await sut.UndoAsync();

        var stored = (await _annotationService.GetAnnotationsAsync(1, 0)).Single();
        Assert.Equal("old", stored.Text);
        Assert.Equal(0.1, stored.Width, 6);

        await sut.RedoAsync();

        stored = (await _annotationService.GetAnnotationsAsync(1, 0)).Single();
        Assert.Equal("a longer text", stored.Text);
        Assert.Equal(0.3, stored.Width, 6);
    }

    [Fact]
    public async Task UpdateAction_WithoutText_LeavesTextUntouched()
    {
        var annotation = await _annotationService.AddTextAsync(1, 0, "keep", "#000000", 0.1, 0.2, 0.1, 0.025);
        var sut = new UpdateAnnotationAction(_annotationService, annotation, (0.1, 0.2, 0.1, 0.025), (0.5, 0.5, 0.1, 0.025));

        await sut.RedoAsync();
        await sut.UndoAsync();

        Assert.Equal("keep", (await _annotationService.GetAnnotationsAsync(1, 0)).Single().Text);
    }

    [Fact]
    public async Task Copy_OfText_KeepsTextAndColor()
    {
        var annotation = await _annotationService.AddTextAsync(1, 0, "dolce", "#ff0000", 0.1, 0.2, 0.1, 0.025);

        var copy = FigureCopy.Copy(annotation);

        Assert.True(copy.IsText);
        Assert.Equal("dolce", copy.Text);
        Assert.Equal("#ff0000", copy.ColorHex);
    }

    [Fact]
    public async Task ExistingDatabaseWithoutTextColumn_IsUpgradedAndKeepsRows()
    {
        using var storage = new TestAppStorageProvider();
        var legacy = new SQLite.SQLiteAsyncConnection(storage.DatabasePath);
        await legacy.ExecuteAsync(
            "CREATE TABLE Annotation (Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, SyncId varchar, SheetId integer, PageIndex integer, " +
            "Type varchar, IconKey varchar, X float, Y float, Width float, Height float, ColorHex varchar, StrokeWidth float, Points varchar, " +
            "CreatedAt bigint, CoordinateSpace integer)");
        await legacy.ExecuteAsync("INSERT INTO Annotation (SyncId, SheetId, PageIndex, Type, IconKey, X, Y, Width, Height, CoordinateSpace) VALUES ('s', 1, 0, 'Icon', 'forte', 0.1, 0.1, 0.1, 0.1, 1)");
        await legacy.CloseAsync();

        var database = new AppDatabase(storage.DatabasePath);
        await database.InitializeAsync();
        var service = new AnnotationService(database);
        await service.AddTextAsync(1, 0, "new", "#000000", 0.2, 0.2, 0.1, 0.025);

        var rows = await service.GetAnnotationsAsync(1, 0);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows.Single(a => a.IconKey == "forte").Text);
        Assert.Equal("new", rows.Single(a => a.IsText).Text);
    }

    [Fact]
    public async Task AddAction_ForText_UndoAndRedo()
    {
        var annotation = await _annotationService.AddTextAsync(1, 0, "x", "#000000", 0.1, 0.2, 0.1, 0.025);
        var page = new ObservableCollection<Annotation> { annotation };
        var sut = new AddAnnotationAction(_annotationService, page, annotation);

        await sut.UndoAsync();
        Assert.Empty(await _annotationService.GetAnnotationsAsync(1, 0));

        await sut.RedoAsync();
        Assert.Equal("x", (await _annotationService.GetAnnotationsAsync(1, 0)).Single().Text);
    }
}
