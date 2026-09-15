using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Annotation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public string IconKey { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public DateTime CreatedAt { get; set; }
}
