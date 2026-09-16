using System.Text.Json;
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
            Type = AnnotationType.Icon,
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

    public async Task<Annotation> AddStrokeAsync(int sheetId, int pageIndex, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)
    {
        var annotation = new Annotation
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Type = AnnotationType.Stroke,
            ColorHex = colorHex,
            StrokeWidth = strokeWidth,
            Points = SerializePoints(points),
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(annotation);
        return annotation;
    }

    public Task UpdateAnnotationAsync(Annotation annotation) =>
        _database.Connection.UpdateAsync(annotation);

    public Task DeleteAnnotationAsync(Annotation annotation) =>
        _database.Connection.DeleteAsync(annotation);

    public static string SerializePoints(IReadOnlyList<StrokePoint> points) =>
        JsonSerializer.Serialize(points);

    public static IReadOnlyList<StrokePoint> DeserializePoints(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<StrokePoint>();
        }

        return JsonSerializer.Deserialize<List<StrokePoint>>(json) ?? new List<StrokePoint>();
    }
}
