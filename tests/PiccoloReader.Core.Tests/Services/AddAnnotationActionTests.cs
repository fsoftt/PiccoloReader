using System.Collections.ObjectModel;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class AddAnnotationActionTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _annotationService;

    public AddAnnotationActionTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _annotationService = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task UndoAsync_RemovesFromDatabaseAndCollection()
    {
        var annotation = await _annotationService.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        var pageAnnotations = new ObservableCollection<Annotation> { annotation };
        var sut = new AddAnnotationAction(_annotationService, pageAnnotations, annotation);

        await sut.UndoAsync();

        Assert.Empty(pageAnnotations);
        Assert.Empty(await _annotationService.GetAnnotationsAsync(1, 0));
    }

    [Fact]
    public async Task RedoAsync_AfterUndo_ReinsertsIntoDatabaseAndCollection()
    {
        var annotation = await _annotationService.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        var pageAnnotations = new ObservableCollection<Annotation> { annotation };
        var sut = new AddAnnotationAction(_annotationService, pageAnnotations, annotation);
        await sut.UndoAsync();

        await sut.RedoAsync();

        Assert.Single(pageAnnotations);
        Assert.Single(await _annotationService.GetAnnotationsAsync(1, 0));
    }
}
