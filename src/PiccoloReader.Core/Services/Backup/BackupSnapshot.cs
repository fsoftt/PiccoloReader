using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services.Backup;

// JSON export of the whole database plus the preferences that matter. Chosen
// over copying the SQLite file because it is tolerant to schema evolution
// (unknown/missing fields) and independent of connection state.
public class BackupSnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public DateTime CreatedUtc { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public List<Folder> Folders { get; set; } = new();

    public List<Sheet> Sheets { get; set; } = new();

    public List<Annotation> Annotations { get; set; } = new();

    public List<Bookmark> Bookmarks { get; set; } = new();

    public List<SheetPageCrop> PageCrops { get; set; } = new();

    public BackupPreferences Preferences { get; set; } = new();
}

public class BackupPreferences
{
    public string? ReadingMode { get; set; }

    public string? LanguageCode { get; set; }

    public bool? SupportWithAds { get; set; }
}

public class BackupInfo
{
    public int Version { get; set; } = 1;

    public DateTime CreatedUtc { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public int FolderCount { get; set; }

    public int SheetCount { get; set; }

    public int PdfCount { get; set; }

    public int AnnotationCount { get; set; }

    public int BookmarkCount { get; set; }
}

public enum BackupStage
{
    Preparing,
    UploadingSheets,
    UploadingLibrary,
    Cleaning,
    DownloadingLibrary,
    DownloadingSheets,
    SafetyCopy,
    Applying
}

public sealed record BackupProgress(BackupStage Stage, int Current, int Total);

public sealed record BackupResult(
    int Uploaded,
    int Unchanged,
    int RemovedFromDrive,
    IReadOnlyList<string> MissingLocalFiles,
    DateTime CompletedUtc);

public sealed record RestoreResult(
    int RestoredSheets,
    IReadOnlyList<string> MissingSheets,
    string SafetyCopyPath,
    bool LanguageChanged);

public class BackupNotFoundException : Exception
{
    public BackupNotFoundException() : base("No backup was found in Google Drive.")
    {
    }
}

public class BackupFormatException : Exception
{
    public BackupFormatException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public enum AutoBackupOutcome
{
    Skipped,
    Completed,
    NeedsSignIn,
    Failed
}
