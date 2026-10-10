using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services.Backup;

public sealed record SheetUpload(Sheet Sheet, string Hash, DriveFile? Existing);

public sealed record BackupPlan(
    IReadOnlyList<SheetUpload> Uploads,
    int Unchanged,
    IReadOnlyList<DriveFile> RemoteOrphans,
    IReadOnlyList<string> MissingLocal);

public static class BackupPlanner
{
    public const string KindProperty = "piccoloKind";
    public const string FileProperty = "piccoloFile";
    public const string HashProperty = "sha256";

    public const string SheetKind = "sheet";
    public const string SnapshotKind = "snapshot";
    public const string InfoKind = "info";

    // Decides what to upload by comparing each local sheet's content hash with the
    // hash stored on its Drive copy (matched by the app's file key, so renaming or
    // moving the file in Drive does not matter; a file deleted in Drive is simply
    // uploaded again). Drive copies of sheets no longer in the library are orphans.
    public static BackupPlan Plan(
        IEnumerable<Sheet> localSheets,
        Func<Sheet, string?> hashOf,
        IReadOnlyList<DriveFile> remoteSheets)
    {
        var remoteByKey = new Dictionary<string, DriveFile>(StringComparer.Ordinal);
        var orphans = new List<DriveFile>();

        foreach (var remote in remoteSheets)
        {
            if (remote.Properties.TryGetValue(FileProperty, out var key) && remoteByKey.TryAdd(key, remote))
            {
                continue;
            }

            // Duplicate copy of the same sheet, or a file without our key.
            orphans.Add(remote);
        }

        var uploads = new List<SheetUpload>();
        var missing = new List<string>();
        var unchanged = 0;
        var liveKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheet in localSheets)
        {
            liveKeys.Add(sheet.FileName);
            var hash = hashOf(sheet);
            if (hash is null)
            {
                missing.Add(sheet.Title);
                continue;
            }

            remoteByKey.TryGetValue(sheet.FileName, out var existing);
            if (existing is not null
                && existing.Properties.TryGetValue(HashProperty, out var remoteHash)
                && string.Equals(remoteHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                continue;
            }

            uploads.Add(new SheetUpload(sheet, hash, existing));
        }

        foreach (var (key, remote) in remoteByKey)
        {
            if (!liveKeys.Contains(key))
            {
                orphans.Add(remote);
            }
        }

        return new BackupPlan(uploads, unchanged, orphans, missing);
    }

    // A readable, unique Drive file name for a sheet title. usedNames holds
    // lower-cased names already taken and is updated.
    public static string UniquePdfName(string title, ISet<string> usedNames)
    {
        var invalid = new HashSet<char>("\\/:*?\"<>|");
        var chars = title.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray();
        var baseName = new string(chars).Trim().TrimEnd('.');
        if (baseName.Length > 100)
        {
            baseName = baseName[..100].TrimEnd();
        }

        if (baseName.Length == 0)
        {
            baseName = "Sheet";
        }

        var candidate = baseName + ".pdf";
        var counter = 2;
        while (!usedNames.Add(candidate.ToLowerInvariant()))
        {
            candidate = $"{baseName} ({counter++}).pdf";
        }

        return candidate;
    }
}
