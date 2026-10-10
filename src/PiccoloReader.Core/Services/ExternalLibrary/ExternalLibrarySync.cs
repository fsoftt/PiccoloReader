using System.Security.Cryptography;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services.ExternalLibrary;

public sealed record SyncProgress(int Current, int Total);

public sealed record SyncAllResult(int Synced, int Failed, bool SnapshotWritten, bool AccessDenied);

// Keeps the external folder (the durable copy) in step with the database:
// copies/moves/deletes the PDFs and rewrites the library snapshot. The app
// keeps reading PDFs from its private cache (Sheets directory); the external
// folder is only read back during recovery.
public class ExternalLibrarySync : ILibraryChangeNotifier
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storage;
    private readonly IExternalLibraryStore _store;
    private readonly IExternalLibraryState _state;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _timerLock = new();

    private CancellationTokenSource? _pendingCts;
    private Task _pendingTask = Task.CompletedTask;

    public ExternalLibrarySync(
        AppDatabase database,
        IAppStorageProvider storage,
        IExternalLibraryStore store,
        IExternalLibraryState state,
        TimeSpan? debounce = null)
    {
        _database = database;
        _storage = storage;
        _store = store;
        _state = state;
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
    }

    public string AppVersion { get; set; } = string.Empty;

    public IExternalLibraryStore Store => _store;

    // Sheets whose PDF has not reached the external folder yet.
    public async Task<int> CountPendingAsync() =>
        await _database.Connection.Table<Sheet>().Where(s => s.ExternalPath == null).CountAsync();

    // Copies the sheet's PDF to its external location, or moves it when the
    // sheet changed folder. False when it could not be done (it stays pending).
    public async Task<bool> SyncSheetAsync(Sheet sheet, bool requestAccess = true)
    {
        await _gate.WaitAsync();
        try
        {
            return await SyncSheetCoreAsync(sheet, requestAccess);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Deletes the sheet's PDF from the external folder (best effort).
    public async Task RemoveSheetAsync(Sheet sheet)
    {
        if (sheet.ExternalPath is null)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (await _store.EnsureAccessAsync(false))
            {
                await _store.DeleteAsync(sheet.ExternalPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"External delete failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }

        NotifyChanged();
    }

    // Migration / reconciliation: pushes every pending sheet (and sheets whose
    // external file vanished or is misplaced) and rewrites the snapshot.
    // Idempotent: a second run finds nothing to do. Failures are counted, never
    // thrown, and retried by the next run.
    public async Task<SyncAllResult> SyncAllAsync(IProgress<SyncProgress>? progress = null, bool requestAccess = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!await _store.EnsureAccessAsync(requestAccess))
            {
                return new SyncAllResult(0, 0, false, true);
            }

            var sheets = await _database.Connection.Table<Sheet>().ToListAsync();
            var synced = 0;
            var failed = 0;
            for (var i = 0; i < sheets.Count; i++)
            {
                progress?.Report(new SyncProgress(i, sheets.Count));
                if (await SyncSheetCoreAsync(sheets[i], false))
                {
                    synced++;
                }
                else
                {
                    failed++;
                }
            }

            progress?.Report(new SyncProgress(sheets.Count, sheets.Count));
            var written = await WriteSnapshotCoreAsync();
            return new SyncAllResult(synced, failed, written, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Debounced: bursts of edits produce one snapshot write.
    public void NotifyChanged()
    {
        lock (_timerLock)
        {
            _pendingCts?.Cancel();
            var cts = new CancellationTokenSource();
            _pendingCts = cts;
            _pendingTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounce, cts.Token);
                    await WriteSnapshotNowAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Snapshot write failed: {ex.Message}");
                }
            });
        }
    }

    // Completes when the latest scheduled snapshot write has finished.
    public Task WaitForPendingAsync()
    {
        lock (_timerLock)
        {
            return _pendingTask;
        }
    }

    public async Task<bool> WriteSnapshotNowAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!await _store.EnsureAccessAsync(false))
            {
                return false;
            }

            return await WriteSnapshotCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LibrarySnapshot> BuildSnapshotAsync()
    {
        var connection = _database.Connection;
        var folders = await connection.Table<Folder>().ToListAsync();
        var sheets = await connection.Table<Sheet>().ToListAsync();
        var annotations = await connection.Table<Annotation>().ToListAsync();
        var bookmarks = await connection.Table<Bookmark>().ToListAsync();
        var crops = await connection.Table<SheetPageCrop>().ToListAsync();

        var folderNames = folders.ToDictionary(f => f.Id, f => f.Name);
        var keyById = sheets.ToDictionary(s => s.Id, s => s.FileName);

        return new LibrarySnapshot
        {
            CreatedUtc = DateTime.UtcNow,
            AppVersion = AppVersion,
            Folders = folders.OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new SnapshotFolder { Name = f.Name, DateAdded = f.DateAdded }).ToList(),
            Sheets = sheets.OrderBy(s => s.FileName, StringComparer.Ordinal).Select(s => new SnapshotSheet
            {
                SheetKey = s.FileName,
                FolderName = s.FolderId is { } id && folderNames.TryGetValue(id, out var name) ? name : null,
                Title = s.Title,
                Path = s.ExternalPath,
                ContentHash = s.ContentHash,
                PageCount = s.PageCount,
                LastViewedPageIndex = s.LastViewedPageIndex,
                DateAdded = s.DateAdded
            }).ToList(),
            Annotations = annotations.Where(a => keyById.ContainsKey(a.SheetId))
                .OrderBy(a => a.SyncId, StringComparer.Ordinal)
                .Select(a => new SnapshotAnnotation
                {
                    SyncId = a.SyncId,
                    SheetKey = keyById[a.SheetId],
                    PageIndex = a.PageIndex,
                    Type = a.Type,
                    IconKey = a.IconKey,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,
                    ColorHex = a.ColorHex,
                    StrokeWidth = a.StrokeWidth,
                    Points = a.Points,
                    CreatedAt = a.CreatedAt,
                    CoordinateSpace = a.CoordinateSpace
                }).ToList(),
            Bookmarks = bookmarks.Where(b => keyById.ContainsKey(b.SheetId))
                .OrderBy(b => b.SyncId, StringComparer.Ordinal)
                .Select(b => new SnapshotBookmark
                {
                    SyncId = b.SyncId,
                    SheetKey = keyById[b.SheetId],
                    PageIndex = b.PageIndex,
                    Name = b.Name,
                    CreatedAt = b.CreatedAt
                }).ToList(),
            PageCrops = crops.Where(c => keyById.ContainsKey(c.SheetId))
                .OrderBy(c => keyById[c.SheetId], StringComparer.Ordinal).ThenBy(c => c.PageIndex)
                .Select(c => new SnapshotCrop
                {
                    SheetKey = keyById[c.SheetId],
                    PageIndex = c.PageIndex,
                    Left = c.Left,
                    Top = c.Top,
                    Right = c.Right,
                    Bottom = c.Bottom
                }).ToList()
        };
    }

    public static async Task<string> ComputeHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    public static async Task<string> ComputeHashAsync(Stream stream) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

    private async Task<bool> WriteSnapshotCoreAsync()
    {
        try
        {
            var snapshot = await BuildSnapshotAsync();
            if (snapshot.Folders.Count == 0 && snapshot.Sheets.Count == 0)
            {
                // Nothing worth saving yet; writing an empty snapshot could
                // also shadow the one of a previous install awaiting recovery.
                return true;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(snapshot.ToJson());
            using var stream = new MemoryStream(bytes);
            _state.SnapshotPath = await _store.WriteAsync(
                _state.SnapshotPath ?? ExternalPaths.SnapshotFileName, stream, ExternalPaths.SnapshotMimeType);
            _state.LastSyncUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Snapshot write failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SyncSheetCoreAsync(Sheet passed, bool requestAccess)
    {
        try
        {
            if (!await _store.EnsureAccessAsync(requestAccess))
            {
                return false;
            }

            // Work on the stored row: the caller's instance may be stale (e.g. a
            // list item loaded before a migration filled in ExternalPath).
            var sheet = await _database.Connection.Table<Sheet>().Where(s => s.Id == passed.Id).FirstOrDefaultAsync();
            if (sheet is null)
            {
                return false;
            }

            var localPath = Path.Combine(_storage.SheetsDirectory, sheet.FileName);
            if (!File.Exists(localPath))
            {
                return false;
            }

            var folderName = sheet.FolderId is { } folderId
                ? (await _database.Connection.Table<Folder>().Where(f => f.Id == folderId).FirstOrDefaultAsync())?.Name
                : null;

            var others = await _database.Connection.Table<Sheet>().Where(s => s.Id != sheet.Id).ToListAsync();
            var taken = new HashSet<string>(
                others.Where(s => s.ExternalPath is not null).Select(s => s.ExternalPath!),
                StringComparer.OrdinalIgnoreCase);

            var desired = ExternalPaths.BuildSheetPath(folderName, sheet.Title, taken);
            var current = sheet.ExternalPath;
            var changed = false;

            if (current is not null && !string.Equals(current, desired, StringComparison.Ordinal)
                && await _store.ExistsAsync(current))
            {
                sheet.ExternalPath = await MoveOrCopyAsync(current, desired, localPath);
                changed = true;
            }
            else if (current is null || !await _store.ExistsAsync(current))
            {
                await using var local = File.OpenRead(localPath);
                sheet.ExternalPath = await _store.WriteAsync(desired, local, ExternalPaths.PdfMimeType);
                changed = true;
            }

            if (sheet.ContentHash is null)
            {
                sheet.ContentHash = await ComputeHashAsync(localPath);
                changed = true;
            }

            if (changed)
            {
                await _database.Connection.UpdateAsync(sheet);
            }

            passed.ExternalPath = sheet.ExternalPath;
            passed.ContentHash = sheet.ContentHash;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"External sync of '{passed.Title}' failed: {ex.Message}");
            return false;
        }
    }

    private async Task<string> MoveOrCopyAsync(string from, string to, string localPath)
    {
        try
        {
            return await _store.MoveAsync(from, to);
        }
        catch (Exception)
        {
            // Not every store/file can be moved (e.g. a file the app does not
            // own): rewrite from the local cache and drop the old copy.
            await using var local = File.OpenRead(localPath);
            var written = await _store.WriteAsync(to, local, ExternalPaths.PdfMimeType);
            await _store.DeleteAsync(from);
            return written;
        }
    }
}
