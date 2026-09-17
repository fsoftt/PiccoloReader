using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Bookmark
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public DateTime CreatedAt { get; set; }
}
