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

    // SHA-256 (hex) of the PDF, used to re-link the file after a reinstall.
    public string? ContentHash { get; set; }

    // Path of the PDF inside the external library folder (Documents/PiccoloReader),
    // relative to it, with "/" separators. Null until copied there.
    public string? ExternalPath { get; set; }
}
