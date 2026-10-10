using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiccoloReader.Core.Services.ExternalLibrary;

// JSON image of the whole library written next to the PDFs. It never contains
// database ids (they are not stable across a reinstall): sheets are referenced
// by SheetKey (the sheet's stable file name) and folders by name.
public class LibrarySnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public DateTime CreatedUtc { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public List<SnapshotFolder> Folders { get; set; } = new();

    public List<SnapshotSheet> Sheets { get; set; } = new();

    public List<SnapshotAnnotation> Annotations { get; set; } = new();

    public List<SnapshotBookmark> Bookmarks { get; set; } = new();

    public List<SnapshotCrop> PageCrops { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    // Null for unreadable or empty content; unknown fields are ignored so
    // newer snapshots still load.
    public static LibrarySnapshot? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LibrarySnapshot>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public class SnapshotFolder
{
    public string Name { get; set; } = string.Empty;

    public DateTime DateAdded { get; set; }
}

public class SnapshotSheet
{
    // Stable identity: the GUID-based file name the sheet has always had.
    public string SheetKey { get; set; } = string.Empty;

    public string? FolderName { get; set; }

    public string Title { get; set; } = string.Empty;

    // Where the PDF sits in the external folder.
    public string? Path { get; set; }

    public string? ContentHash { get; set; }

    public int PageCount { get; set; }

    public int LastViewedPageIndex { get; set; }

    public DateTime DateAdded { get; set; }
}

public class SnapshotAnnotation
{
    public string SyncId { get; set; } = string.Empty;

    public string SheetKey { get; set; } = string.Empty;

    public int PageIndex { get; set; }

    public string? Type { get; set; }

    public string? IconKey { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string? ColorHex { get; set; }

    public double StrokeWidth { get; set; }

    public string? Points { get; set; }

    public DateTime CreatedAt { get; set; }

    public int CoordinateSpace { get; set; }
}

public class SnapshotBookmark
{
    public string SyncId { get; set; } = string.Empty;

    public string SheetKey { get; set; } = string.Empty;

    public int PageIndex { get; set; }

    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class SnapshotCrop
{
    public string SheetKey { get; set; } = string.Empty;

    public int PageIndex { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Right { get; set; }

    public double Bottom { get; set; }
}
