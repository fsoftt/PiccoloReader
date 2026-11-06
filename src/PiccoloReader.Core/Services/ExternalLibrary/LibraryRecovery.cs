using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services.ExternalLibrary;

public sealed record RecoveryResult(
    bool SnapshotFound,
    int FoldersAdded,
    int SheetsRestored,
    int SheetsAlreadyPresent,
    int OrphansImported,
    int AnnotationsAdded,
    int BookmarksAdded,
    int CropsAdded,
    IReadOnlyList<string> MissingSheets)
{
    public bool FoundAnything => SnapshotFound || SheetsRestored + OrphansImported > 0;
}

// Rebuilds the database from the external folder after a reinstall. A merge,
// not a replace: GUIDs / hashes / paths make a second run a no-op.
public class LibraryRecovery
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storage;
    private readonly IPdfPageRenderer? _pageRenderer;

    public LibraryRecovery(AppDatabase database, IAppStorageProvider storage, IPdfPageRenderer? pageRenderer = null)
    {
        _database = database;
        _storage = storage;
        _pageRenderer = pageRenderer;
    }

    public async Task<RecoveryResult> RecoverAsync(
        IExternalLibraryStore source,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var connection = _database.Connection;
        Directory.CreateDirectory(_storage.SheetsDirectory);

        var files = await source.ListAsync();
        var pdfPaths = files.Where(ExternalPaths.IsPdf).ToList();
        var pathLookup = pdfPaths.ToDictionary(p => p, StringComparer.OrdinalIgnoreCase);

        var snapshot = await ReadSnapshotAsync(source, files, cancellationToken);

        // Folders.
        var folders = await connection.Table<Folder>().ToListAsync();
        var foldersAdded = 0;
        async Task<int> EnsureFolderAsync(string name, DateTime dateAdded)
        {
            var existing = folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing.Id;
            }

            var folder = new Folder { Name = name, DateAdded = dateAdded };
            await connection.InsertAsync(folder);
            folders.Add(folder);
            foldersAdded++;
            return folder.Id;
        }

        if (snapshot is not null)
        {
            foreach (var folder in snapshot.Folders.Where(f => !string.IsNullOrWhiteSpace(f.Name)))
            {
                await EnsureFolderAsync(folder.Name, folder.DateAdded);
            }
        }

        var existingSheets = await connection.Table<Sheet>().ToListAsync();
        var sheetByKey = existingSheets.ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
        var claimedPaths = new HashSet<string>(
            existingSheets.Where(s => s.ExternalPath is not null).Select(s => s.ExternalPath!),
            StringComparer.OrdinalIgnoreCase);

        var hashCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        async Task<string?> HashOfAsync(string path)
        {
            if (hashCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            await using var stream = await source.OpenReadAsync(path, cancellationToken);
            var hash = stream is null ? null : await ExternalLibrarySync.ComputeHashAsync(stream);
            hashCache[path] = hash;
            return hash;
        }

        var total = (snapshot?.Sheets.Count ?? 0) + pdfPaths.Count;
        var step = 0;
        var restored = 0;
        var alreadyPresent = 0;
        var missing = new List<string>();

        // Sheets known to the snapshot.
        foreach (var entry in snapshot?.Sheets ?? new List<SnapshotSheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SyncProgress(step++, total));

            if (string.IsNullOrWhiteSpace(entry.SheetKey))
            {
                continue;
            }

            if (sheetByKey.ContainsKey(entry.SheetKey))
            {
                alreadyPresent++;
                if (entry.Path is not null)
                {
                    claimedPaths.Add(entry.Path);
                }

                continue;
            }

            var foundPath = await LocateAsync(entry, pathLookup, claimedPaths, pdfPaths, HashOfAsync);
            if (foundPath is null)
            {
                missing.Add(entry.Title);
                continue;
            }

            var localPath = Path.Combine(_storage.SheetsDirectory, entry.SheetKey);
            var hash = await CopyToLocalAsync(source, foundPath, localPath, cancellationToken);
            if (hash is null)
            {
                missing.Add(entry.Title);
                continue;
            }

            int? folderId = string.IsNullOrWhiteSpace(entry.FolderName)
                ? null
                : await EnsureFolderAsync(entry.FolderName, entry.DateAdded);

            var sheet = new Sheet
            {
                FolderId = folderId,
                Title = entry.Title,
                FileName = entry.SheetKey,
                PageCount = entry.PageCount > 0
                    ? entry.PageCount
                    : await ExternalLibrarySync.CountPagesAsync(_pageRenderer, localPath),
                LastViewedPageIndex = entry.LastViewedPageIndex,
                DateAdded = entry.DateAdded,
                SortOrder = entry.SortOrder,
                ContentHash = hash,
                ExternalPath = foundPath
            };
            await connection.InsertAsync(sheet);
            sheetByKey[sheet.FileName] = sheet;
            claimedPaths.Add(foundPath);
            restored++;
        }

        // PDFs nobody claims (no snapshot, or files the user dropped in the folder).
        var orphans = 0;
        foreach (var path in pdfPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SyncProgress(step++, total));

            if (claimedPaths.Contains(path))
            {
                continue;
            }

            var fileName = $"{Guid.NewGuid():N}.pdf";
            var localPath = Path.Combine(_storage.SheetsDirectory, fileName);
            var hash = await CopyToLocalAsync(source, path, localPath, cancellationToken);
            if (hash is null)
            {
                continue;
            }

            var directory = ExternalPaths.DirectoryOf(path);
            int? folderId = directory is null ? null : await EnsureFolderAsync(directory, DateTime.UtcNow);

            var sheet = new Sheet
            {
                FolderId = folderId,
                Title = Path.GetFileNameWithoutExtension(ExternalPaths.FileNameOf(path)),
                FileName = fileName,
                PageCount = await ExternalLibrarySync.CountPagesAsync(_pageRenderer, localPath),
                DateAdded = DateTime.UtcNow,
                SortOrder = await SheetOrdering.NextSortOrderAsync(connection, folderId),
                ContentHash = hash,
                ExternalPath = path
            };
            await connection.InsertAsync(sheet);
            sheetByKey[sheet.FileName] = sheet;
            claimedPaths.Add(path);
            orphans++;
        }

        progress?.Report(new SyncProgress(total, total));

        // Annotations, bookmarks and crops of the snapshot, de-duplicated.
        var annotationsAdded = 0;
        var bookmarksAdded = 0;
        var cropsAdded = 0;

        if (snapshot is not null)
        {
            var knownAnnotations = (await connection.Table<Annotation>().ToListAsync())
                .Select(a => a.SyncId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var a in snapshot.Annotations)
            {
                if (!sheetByKey.TryGetValue(a.SheetKey, out var sheet) || string.IsNullOrEmpty(a.SyncId)
                    || !knownAnnotations.Add(a.SyncId))
                {
                    continue;
                }

                await connection.InsertAsync(new Annotation
                {
                    SyncId = a.SyncId,
                    SheetId = sheet.Id,
                    PageIndex = a.PageIndex,
                    Type = a.Type ?? AnnotationType.Icon,
                    IconKey = a.IconKey ?? string.Empty,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,
                    ColorHex = a.ColorHex,
                    StrokeWidth = a.StrokeWidth,
                    Points = a.Points,
                    Text = a.Text,
                    CreatedAt = a.CreatedAt,
                    CoordinateSpace = a.CoordinateSpace
                });
                annotationsAdded++;
            }

            var knownBookmarks = (await connection.Table<Bookmark>().ToListAsync())
                .Select(b => b.SyncId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var b in snapshot.Bookmarks)
            {
                if (!sheetByKey.TryGetValue(b.SheetKey, out var sheet) || string.IsNullOrEmpty(b.SyncId)
                    || !knownBookmarks.Add(b.SyncId))
                {
                    continue;
                }

                await connection.InsertAsync(new Bookmark
                {
                    SyncId = b.SyncId,
                    SheetId = sheet.Id,
                    PageIndex = b.PageIndex,
                    Name = b.Name,
                    CreatedAt = b.CreatedAt
                });
                bookmarksAdded++;
            }

            var knownCrops = (await connection.Table<SheetPageCrop>().ToListAsync())
                .Select(c => (c.SheetId, c.PageIndex)).ToHashSet();
            foreach (var c in snapshot.PageCrops)
            {
                if (!sheetByKey.TryGetValue(c.SheetKey, out var sheet) || !knownCrops.Add((sheet.Id, c.PageIndex)))
                {
                    continue;
                }

                await connection.InsertAsync(new SheetPageCrop
                {
                    SheetId = sheet.Id,
                    PageIndex = c.PageIndex,
                    Left = c.Left,
                    Top = c.Top,
                    Right = c.Right,
                    Bottom = c.Bottom
                });
                cropsAdded++;
            }
        }

        return new RecoveryResult(
            snapshot is not null, foldersAdded, restored, alreadyPresent, orphans,
            annotationsAdded, bookmarksAdded, cropsAdded, missing);
    }

    // On Android 10+ a reinstalled app cannot see the snapshot of its previous
    // install, so each install writes "piccolo-library (n).json" next to the
    // older ones. Read every candidate and keep the newest valid one: by the
    // CreatedUtc stored inside (written by every sync), then by the highest (n)
    // (the plain name counts as 0) when timestamps tie.
    private static readonly System.Text.RegularExpressions.Regex SnapshotNamePattern = new(
        @"^piccolo-library(?: \((\d+)\))?\.json$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static async Task<LibrarySnapshot?> ReadSnapshotAsync(
        IExternalLibraryStore source, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        LibrarySnapshot? best = null;
        var bestNumber = -1;
        foreach (var file in files)
        {
            var match = SnapshotNamePattern.Match(file);
            if (!match.Success)
            {
                continue;
            }

            var number = match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
            LibrarySnapshot? snapshot;
            try
            {
                await using var stream = await source.OpenReadAsync(file, cancellationToken);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                snapshot = LibrarySnapshot.TryParse(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"Snapshot {file} unreadable: {ex.Message}");
                continue;
            }

            if (snapshot is null)
            {
                continue;
            }

            if (best is null
                || snapshot.CreatedUtc > best.CreatedUtc
                || (snapshot.CreatedUtc == best.CreatedUtc && number > bestNumber))
            {
                best = snapshot;
                bestNumber = number;
            }
        }

        return best;
    }

    // The PDF of a snapshot sheet: its recorded path if it still holds the same
    // content, otherwise any unclaimed PDF with the same hash (moved/renamed by
    // the user), otherwise the file at the recorded path as it is now.
    private static async Task<string?> LocateAsync(
        SnapshotSheet entry,
        Dictionary<string, string> pathLookup,
        HashSet<string> claimedPaths,
        List<string> pdfPaths,
        Func<string, Task<string?>> hashOf)
    {
        string? atPath = null;
        if (entry.Path is not null && pathLookup.TryGetValue(entry.Path, out var actual) && !claimedPaths.Contains(actual))
        {
            atPath = actual;
            if (entry.ContentHash is null || string.Equals(await hashOf(actual), entry.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return actual;
            }
        }

        if (entry.ContentHash is not null)
        {
            foreach (var candidate in pdfPaths.Where(p => !claimedPaths.Contains(p) && p != atPath))
            {
                if (string.Equals(await hashOf(candidate), entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return atPath;
    }

    // Copies a store file into the app cache and returns its hash (null when unreadable).
    private static async Task<string?> CopyToLocalAsync(
        IExternalLibraryStore source, string path, string localPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = await source.OpenReadAsync(path, cancellationToken);
            if (input is null)
            {
                return null;
            }

            await using (var output = File.Create(localPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            return await ExternalLibrarySync.ComputeHashAsync(localPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }

            return null;
        }
    }
}
