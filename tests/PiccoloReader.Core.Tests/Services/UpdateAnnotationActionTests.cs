using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class UpdateAnnotationActionTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _annotationService;

    public UpdateAnnotationActionTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _annotationService = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task UndoAsync_RestoresBeforeValuesAndPersists()
    {
        var annotation = await _annotationService.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.2, 0.2);
        annotation.X = 0.5;
        annotation.Y = 0.5;
        var sut = new UpdateAnnotationAction(_annotationService, annotation, before: (0.1, 0.1, 0.2, 0.2), after: (0.5, 0.5, 0.2, 0.2));

        await sut.UndoAsync();

        Assert.Equal(0.1, annotation.X);
        Assert.Equal(0.1, annotation.Y);
        var loaded = await _annotationService.GetAnnotationsAsync(1, 0);
        Assert.Equal(0.1, loaded[0].X);
    }

    [Fact]
    public async Task RedoAsync_AppliesAfterValuesAndPersists()
    {
        var annotation = await _annotationService.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.2, 0.2);
        var sut = new UpdateAnnotationAction(_annotationService, annotation, before: (0.1, 0.1, 0.2, 0.2), after: (0.5, 0.5, 0.3, 0.3));

        await sut.RedoAsync();

        Assert.Equal(0.5, annotation.X);
        Assert.Equal(0.3, annotation.Width);
        var loaded = await _annotationService.GetAnnotationsAsync(1, 0);
        Assert.Equal(0.5, loaded[0].X);
        Assert.Equal(0.3, loaded[0].Width);
    }
}
