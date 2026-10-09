using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Annotation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public string Type { get; set; } = AnnotationType.Icon;

    public string IconKey { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string? ColorHex { get; set; }

    public double StrokeWidth { get; set; }

    public string? Points { get; set; }

    public DateTime CreatedAt { get; set; }

    // Which rectangle X/Y/Width/Height, stroke points and StrokeWidth are
    // normalized (0-1) to: AnnotationCoordinateSpace.Legacy (0) is the
    // reading area container, Page (1) is the page itself. Rows from before
    // the column existed read back as 0 and are converted lazily when they
    // are first shown on a page of known aspect ratio.
    public int CoordinateSpace { get; set; }

    // Existing rows predate the Type column and read back with Type
    // null/empty from SQLite (sqlite-net-pcl overwrites the field
    // initializer's default with the column's actual NULL value on
    // read, so the initializer alone doesn't cover old rows) - treating
    // anything that isn't explicitly "Stroke" as an icon means old rows
    // keep working with no backfill migration needed.
    public bool IsStroke => Type == AnnotationType.Stroke;
}

public static class AnnotationType
{
    public const string Icon = "Icon";
    public const string Stroke = "Stroke";
}

public static class AnnotationCoordinateSpace
{
    public const int Legacy = 0;
    public const int Page = 1;
}
