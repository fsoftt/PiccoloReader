using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class AnnotationService
{
    private readonly AppDatabase _database;

    public AnnotationService(AppDatabase database)
    {
        _database = database;
    }

    public Task<List<Annotation>> GetAnnotationsAsync(int sheetId, int pageIndex) =>
        _database.Connection.Table<Annotation>()
            .Where(a => a.SheetId == sheetId && a.PageIndex == pageIndex)
            .ToListAsync();

    public async Task<Annotation> AddIconAsync(int sheetId, int pageIndex, string iconKey, double x, double y, double width, double height)
    {
        var annotation = new Annotation
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            IconKey = iconKey,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(annotation);
        return annotation;
    }

    public Task UpdateAnnotationAsync(Annotation annotation) =>
        _database.Connection.UpdateAsync(annotation);

    public Task DeleteAnnotationAsync(Annotation annotation) =>
        _database.Connection.DeleteAsync(annotation);
}
