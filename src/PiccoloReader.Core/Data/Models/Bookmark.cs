using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Bookmark
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Stable identity across devices/reinstalls: lets a library recovered from the
    // external folder merge without duplicating the same item. Rows from before the
    // column existed are backfilled on startup.
    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; }
}
