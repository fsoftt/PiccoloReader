using PiccoloReader.Core.Data;
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
}
