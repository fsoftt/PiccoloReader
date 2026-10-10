using System.Text;

namespace PiccoloReader.Core.Services.ExternalLibrary;

public static class ExternalPaths
{
    public const string SnapshotFileName = "piccolo-library.json";

    public const string SnapshotMimeType = "application/json";

    public const string PdfMimeType = "application/pdf";

    private const int MaxSegmentLength = 100;

    private const string InvalidChars = "/\\:*?\"<>|";

    // A file or directory name that is valid on every Android file system.
    public static string SanitizeSegment(string? name, string fallback)
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            builder.Append(InvalidChars.Contains(c) || char.IsControl(c) ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length > MaxSegmentLength)
        {
            cleaned = cleaned[..MaxSegmentLength].TrimEnd();
        }

        return cleaned.Length == 0 ? fallback : cleaned;
    }

    // "Folder/Title.pdf" (or "Title.pdf" at the root). When the path is taken
    // by another sheet, " (2)", " (3)"... is appended to the title.
    public static string BuildSheetPath(string? folderName, string title, ISet<string> takenPaths)
    {
        var directory = folderName is null ? string.Empty : SanitizeSegment(folderName, "Folder") + "/";
        var stem = SanitizeSegment(title, "Untitled");

        var candidate = $"{directory}{stem}.pdf";
        for (var n = 2; takenPaths.Contains(candidate); n++)
        {
            candidate = $"{directory}{stem} ({n}).pdf";
        }

        return candidate;
    }

    public static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public static string? DirectoryOf(string path)
    {
        var index = path.IndexOf('/');
        return index < 0 ? null : path[..index];
    }

    public static string FileNameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
}
