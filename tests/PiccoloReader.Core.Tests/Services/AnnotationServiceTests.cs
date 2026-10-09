using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class AnnotationServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _sut;

    public AnnotationServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task AddIconAsync_InsertsRetrievableAnnotation()
    {
        var annotation = await _sut.AddIconAsync(sheetId: 1, pageIndex: 0, iconKey: "dynamicForte", x: 0.4, y: 0.5, width: 0.1, height: 0.09);

        var loaded = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

        Assert.Single(loaded);
        Assert.Equal(annotation.Id, loaded[0].Id);
        Assert.Equal("dynamicForte", loaded[0].IconKey);
        Assert.Equal(0.4, loaded[0].X);
    }

    [Fact]
    public async Task GetAnnotationsAsync_FiltersByPageIndex()
    {
        await _sut.AddIconAsync(1, pageIndex: 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        await _sut.AddIconAsync(1, pageIndex: 1, "dynamicPiano", 0.2, 0.2, 0.1, 0.1);

        var page0 = await _sut.GetAnnotationsAsync(1, pageIndex: 0);
        var page1 = await _sut.GetAnnotationsAsync(1, pageIndex: 1);

        Assert.Single(page0);
        Assert.Single(page1);
        Assert.Equal("dynamicForte", page0[0].IconKey);
        Assert.Equal("dynamicPiano", page1[0].IconKey);
    }

    [Fact]
    public async Task GetAnnotationsAsync_FiltersBySheetId()
    {
        await _sut.AddIconAsync(sheetId: 1, pageIndex: 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        await _sut.AddIconAsync(sheetId: 2, pageIndex: 0, "dynamicPiano", 0.2, 0.2, 0.1, 0.1);

        var sheet1 = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

        Assert.Single(sheet1);
        Assert.Equal("dynamicForte", sheet1[0].IconKey);
    }

    [Fact]
    public async Task UpdateAnnotationAsync_PersistsPositionChange()
    {
        var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        annotation.X = 0.6;
        annotation.Y = 0.7;

        await _sut.UpdateAnnotationAsync(annotation);

        var loaded = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(0.6, loaded[0].X);
        Assert.Equal(0.7, loaded[0].Y);
    }

    [Fact]
    public async Task DeleteAnnotationAsync_RemovesRow()
    {
        var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

        await _sut.DeleteAnnotationAsync(annotation);

        var loaded = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task AddStrokeAsync_InsertsRetrievableStroke()
    {
        var points = new List<StrokePoint> { new(0.1, 0.1), new(0.2, 0.2), new(0.3, 0.15) };

        var annotation = await _sut.AddStrokeAsync(sheetId: 1, pageIndex: 0, colorHex: "#FF0000", strokeWidth: 0.01, points: points);

        var loaded = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

        Assert.Single(loaded);
        Assert.Equal(annotation.Id, loaded[0].Id);
        Assert.Equal(AnnotationType.Stroke, loaded[0].Type);
        Assert.True(loaded[0].IsStroke);
        Assert.Equal("#FF0000", loaded[0].ColorHex);
        Assert.Equal(0.01, loaded[0].StrokeWidth);

        var roundTrippedPoints = AnnotationService.DeserializePoints(loaded[0].Points);
        Assert.Equal(3, roundTrippedPoints.Count);
        Assert.Equal(0.1, roundTrippedPoints[0].X);
        Assert.Equal(0.1, roundTrippedPoints[0].Y);
        Assert.Equal(0.3, roundTrippedPoints[2].X);
    }

    [Fact]
    public async Task AddIconAsync_SetsTypeToIcon()
    {
        var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

        Assert.Equal(AnnotationType.Icon, annotation.Type);
        Assert.False(annotation.IsStroke);
    }

    [Fact]
    public void Annotation_WithNullType_IsTreatedAsIcon()
    {
        var annotation = new Annotation { Type = null! };

        Assert.False(annotation.IsStroke);
    }

    [Fact]
    public void SerializePoints_DeserializePoints_RoundTrips()
    {
        var points = new List<StrokePoint> { new(0.0, 0.0), new(1.0, 1.0) };

        var json = AnnotationService.SerializePoints(points);
        var result = AnnotationService.DeserializePoints(json);

        Assert.Equal(2, result.Count);
        Assert.Equal(points[0], result[0]);
        Assert.Equal(points[1], result[1]);
    }

    [Fact]
    public void DeserializePoints_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(AnnotationService.DeserializePoints(null));
        Assert.Empty(AnnotationService.DeserializePoints(""));
    }

    [Fact]
    public async Task InsertAnnotationAsync_InsertsRetrievableAnnotation()
    {
        var annotation = new Annotation
        {
            SheetId = 1,
            PageIndex = 0,
            Type = AnnotationType.Icon,
            IconKey = "dynamicForte",
            X = 0.1,
            Y = 0.1,
            Width = 0.1,
            Height = 0.1,
            CreatedAt = DateTime.UtcNow
        };

        await _sut.InsertAnnotationAsync(annotation);

        var loaded = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Single(loaded);
        Assert.Equal("dynamicForte", loaded[0].IconKey);
    }

    [Fact]
    public async Task AddIconAndStroke_AreStoredInPageSpace()
    {
        await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        await _sut.AddStrokeAsync(1, 0, "#000000", 0.01, new List<StrokePoint> { new(0, 0), new(1, 1) });

        var loaded = await _sut.GetAnnotationsAsync(1, 0);

        Assert.All(loaded, a => Assert.Equal(AnnotationCoordinateSpace.Page, a.CoordinateSpace));
    }

    [Fact]
    public async Task GetPageAnnotationsAsync_LegacyRow_IsConvertedAndPersistedOnce()
    {
        // Legacy icon: 400x800 reference container, 400x600 page (y offset 0.125).
        await _sut.InsertAnnotationAsync(new Annotation
        {
            SheetId = 1,
            PageIndex = 0,
            Type = AnnotationType.Icon,
            IconKey = "dynamicForte",
            X = 0.5,
            Y = 0.5,
            Width = 0.1,
            Height = 0.075,
            CreatedAt = DateTime.UtcNow
        });

        var first = await _sut.GetPageAnnotationsAsync(1, 0, 1.5, 800, 400);
        Assert.Equal(0.5, first[0].Y, 6);
        Assert.Equal(0.1, first[0].Height, 6);
        Assert.Equal(AnnotationCoordinateSpace.Page, first[0].CoordinateSpace);

        // A later load with a different container/page must not convert again.
        var second = await _sut.GetPageAnnotationsAsync(1, 0, 1.0, 300, 900);
        Assert.Equal(0.5, second[0].Y, 6);
        Assert.Equal(0.1, second[0].Height, 6);

        var raw = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(AnnotationCoordinateSpace.Page, raw[0].CoordinateSpace);
    }

    [Fact]
    public async Task GetPageAnnotationsAsync_UnknownAspect_LeavesLegacyRowForLater()
    {
        await _sut.InsertAnnotationAsync(new Annotation
        {
            SheetId = 1,
            PageIndex = 0,
            Type = AnnotationType.Icon,
            IconKey = "dynamicForte",
            X = 0.5,
            Y = 0.5,
            Width = 0.1,
            Height = 0.075,
            CreatedAt = DateTime.UtcNow
        });

        await _sut.GetPageAnnotationsAsync(1, 0, 0, 400, 800);

        var raw = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(AnnotationCoordinateSpace.Legacy, raw[0].CoordinateSpace);
    }

    private Task InsertLegacyIconAsync(int pageIndex) => _sut.InsertAnnotationAsync(new Annotation
    {
        SheetId = 1,
        PageIndex = pageIndex,
        Type = AnnotationType.Icon,
        IconKey = "dynamicForte",
        X = 0.5,
        Y = 0.5,
        Width = 0.12,
        Height = 0.141,
        CreatedAt = DateTime.UtcNow
    });

    [Fact]
    public async Task GetPageAnnotationsAsync_SameLegacyRowOnDifferentPages_ConvertsIdentically()
    {
        // The same legacy icon on two pages, loaded at different "times" with
        // the same stable reference (dp, shape only), must convert identically.
        await InsertLegacyIconAsync(0);
        await InsertLegacyIconAsync(1);

        var firstPage = await _sut.GetPageAnnotationsAsync(1, 0, 1.4142, 411, 780);
        var secondPage = await _sut.GetPageAnnotationsAsync(1, 1, 1.4142, 411, 780);

        Assert.Equal(firstPage[0].Y, secondPage[0].Y, 9);
        Assert.Equal(firstPage[0].Height, secondPage[0].Height, 9);
        Assert.Equal(firstPage[0].Width, secondPage[0].Width, 9);
    }

    [Fact]
    public async Task GetPageAnnotationsAsync_ReferenceIsUnitIndependent()
    {
        // dp vs px of the same reference area gives the same result.
        await InsertLegacyIconAsync(0);
        await InsertLegacyIconAsync(1);

        var dp = await _sut.GetPageAnnotationsAsync(1, 0, 1.4142, 411, 780);
        var px = await _sut.GetPageAnnotationsAsync(1, 1, 1.4142, 411 * 2.625, 780 * 2.625);

        Assert.Equal(dp[0].Y, px[0].Y, 9);
        Assert.Equal(dp[0].Height, px[0].Height, 9);
    }

    [Fact]
    public async Task GetPageAnnotationsAsync_PersistFalse_ConvertsForDisplayButKeepsRowLegacy()
    {
        await InsertLegacyIconAsync(0);

        var shown = await _sut.GetPageAnnotationsAsync(1, 0, 1.4142, 1080, 2400, persist: false);
        Assert.Equal(AnnotationCoordinateSpace.Page, shown[0].CoordinateSpace);

        var raw = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(AnnotationCoordinateSpace.Legacy, raw[0].CoordinateSpace);
        Assert.Equal(0.141, raw[0].Height, 9);

        // A later load with the stable reference converts and saves it.
        var stable = await _sut.GetPageAnnotationsAsync(1, 0, 1.4142, 411, 780);
        raw = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(AnnotationCoordinateSpace.Page, raw[0].CoordinateSpace);
        Assert.Equal(stable[0].Height, raw[0].Height, 9);
    }
}
