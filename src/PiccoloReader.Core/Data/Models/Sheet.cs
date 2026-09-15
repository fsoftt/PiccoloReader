using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Sheet
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? FolderId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public int LastViewedPageIndex { get; set; }

    public DateTime DateAdded { get; set; }
}
