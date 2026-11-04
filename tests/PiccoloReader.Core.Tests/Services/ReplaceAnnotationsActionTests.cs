using System.Collections.ObjectModel;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class ReplaceAnnotationsActionTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _annotationService;

    public ReplaceAnnotationsActionTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _annotationService = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    private static IReadOnlyList<StrokePoint> Points() => new[] { new StrokePoint(0.1, 0.1), new StrokePoint(0.2, 0.1) };

    [Fact]
    public async Task UndoAsync_RestoresUnsavedStrokeAndRemovesFigure()
    {
        // The state right after a recognition: the figure is saved, the stroke never was.
        var stroke = RecognizedFigure.UnsavedStroke(1, 0, "#000000", 0.005, Points());
        var figure = await _annotationService.AddIconAsync(1, 0, SlurGeometry.AboveKey, 0.1, 0.1, 0.1, 0.03);
        var page = new ObservableCollection<Annotation> { figure };
        var sut = new ReplaceAnnotationsAction(_annotationService, page, new[] { stroke }, new[] { figure });

        await sut.UndoAsync();

        var stored = await _annotationService.GetAnnotationsAsync(1, 0);
        Assert.Single(stored);
        Assert.True(stored[0].IsStroke);
        Assert.Single(page);
        Assert.True(page[0].IsStroke);
    }

    [Fact]
    public async Task RedoAsync_ReappliesFigure()
    {
        var stroke = RecognizedFigure.UnsavedStroke(1, 0, "#000000", 0.005, Points());
        var figure = await _annotationService.AddIconAsync(1, 0, SlurGeometry.AboveKey, 0.1, 0.1, 0.1, 0.03);
        var page = new ObservableCollection<Annotation> { figure };
        var sut = new ReplaceAnnotationsAction(_annotationService, page, new[] { stroke }, new[] { figure });
        await sut.UndoAsync();

        await sut.RedoAsync();

        var stored = await _annotationService.GetAnnotationsAsync(1, 0);
        Assert.Single(stored);
        Assert.False(stored[0].IsStroke);
        Assert.Single(page);
        Assert.Equal(SlurGeometry.AboveKey, page[0].IconKey);
    }

    [Fact]
    public async Task UndoAsync_TwoStrokesJoinedIntoFigure_RestoresBoth()
    {
        var first = await _annotationService.AddStrokeAsync(1, 0, "#000000", 0.005, Points());
        var second = RecognizedFigure.UnsavedStroke(1, 0, "#000000", 0.005, Points());
        await _annotationService.DeleteAnnotationAsync(first);
        var figure = await _annotationService.AddIconAsync(1, 0, HairpinGeometry.CrescendoKey, 0.1, 0.1, 0.1, 0.03);
        var page = new ObservableCollection<Annotation> { figure };
        var sut = new ReplaceAnnotationsAction(_annotationService, page, new[] { first, second }, new[] { figure });

        await sut.UndoAsync();

        Assert.Equal(2, (await _annotationService.GetAnnotationsAsync(1, 0)).Count);
        Assert.Equal(2, page.Count);
        Assert.All(page, a => Assert.True(a.IsStroke));
    }

    [Fact]
    public void RecognizedFigure_Slur_BoxIsCurveBoundsAndKeepsColorAndPoints()
    {
        var shape = ShapeRecognizer.Recognize(Enumerable.Range(0, 41).Select(i =>
        {
            var t = i / 40.0;
            return new StrokePoint(0.2 + 0.2 * t, 0.5 - 4 * 0.04 * t * (1 - t) / 1.4);
        }).ToList(), 1.4);

        var figure = RecognizedFigure.Create(shape, 7, 3, "#FF0000", PageCrop.Full);

        Assert.NotNull(figure);
        Assert.Equal(SlurGeometry.AboveKey, figure!.IconKey);
        Assert.Equal("#FF0000", figure.ColorHex);
        Assert.Equal(7, figure.SheetId);
        Assert.Equal(3, figure.PageIndex);
        Assert.Equal(4, SlurGeometry.ControlPoints(figure).Count);
        Assert.InRange(figure.X, 0.19, 0.21);
    }

    [Fact]
    public void RecognizedFigure_OutsideCrop_IsShiftedInside()
    {
        var shape = new RecognizedShape(RecognizedShapeKind.Hairpin, HairpinKey: HairpinGeometry.CrescendoKey, X: 0.05, Y: 0.5, Width: 0.1, Height: 0.03);

        var figure = RecognizedFigure.Create(shape, 1, 0, "#000000", new PageCrop(0.2, 0.2, 0.9, 0.9));

        Assert.Equal(0.2, figure!.X, 6);
    }

    [Fact]
    public void RecognizedFigure_NothingRecognized_ReturnsNull()
    {
        Assert.Null(RecognizedFigure.Create(RecognizedShape.None, 1, 0, "#000000", PageCrop.Full));
    }
}
