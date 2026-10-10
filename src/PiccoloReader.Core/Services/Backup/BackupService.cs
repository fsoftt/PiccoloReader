using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services.Backup;

// Backs the library up to / restores it from the user's Google Drive:
//   PiccoloReader/Partituras/<title>.pdf   one file per sheet (incremental, by SHA-256)
//   PiccoloReader/piccolo-library.json     snapshot of folders, sheets, annotations,
//                                          bookmarks, page crops and preferences
//   PiccoloReader/backup-info.json         date, app version, counts
public class BackupService
{
    public const string RootFolderName = "PiccoloReader";
    public const string SheetsFolderName = "Partituras";
    public const string SnapshotFileName = "piccolo-library.json";
    public const string InfoFileName = "backup-info.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storage;
    private readonly IDriveClient _drive;
    private readonly IReadingPreferenceService _reading;
    private readonly ILanguagePreferenceService _language;
    private readonly IAdsPreferenceService _ads;
    private readonly IBackupStateStore _state;
    private readonly BackupOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(
        AppDatabase database,
        IAppStorageProvider storage,
        IDriveClient drive,
        IReadingPreferenceService reading,
        ILanguagePreferenceService language,
        IAdsPreferenceService ads,
        IBackupStateStore state,
        BackupOptions options)
    {
        _database = database;
        _storage = storage;
        _drive = drive;
        _reading = reading;
        _language = language;
        _ads = ads;
        _state = state;
        _options = options;
    }

    // Raised after a restore replaced the library, so open screens can reload.
    public event EventHandler? LibraryRestored;

    public bool IsBusy => _gate.CurrentCount == 0;

    private string DataDirectory => Path.GetDirectoryName(_storage.DatabasePath)!;

    // ---------------------------------------------------------------- backup

    public async Task<BackupResult> BackupAsync(IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await BackupCoreAsync(progress, cancellationToken);
            _state.LastBackupUtc = result.CompletedUtc;
            _state.LastBackupError = null;
            _state.NeedsSignIn = false;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.LastBackupError = ex.Message;
            if (ex is DriveAuthRequiredException)
            {
                _state.NeedsSignIn = true;
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Used by the periodic worker: never shows UI and only throws on cancellation.
    public async Task<AutoBackupOutcome> RunAutomaticBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.AutoBackupEnabled || !_state.IsConnected)
        {
            return AutoBackupOutcome.Skipped;
        }

        try
        {
            await BackupAsync(null, cancellationToken);
            return AutoBackupOutcome.Completed;
        }
        catch (DriveAuthRequiredException)
        {
            return AutoBackupOutcome.NeedsSignIn;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return AutoBackupOutcome.Failed;
        }
    }

    private async Task<BackupResult> BackupCoreAsync(IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new BackupProgress(BackupStage.Preparing, 0, 0));

        var snapshot = await BuildSnapshotAsync();
        var rootId = await _drive.EnsureFolderAsync(new[] { RootFolderName }, ct);
        var sheetsFolderId = await _drive.EnsureFolderAsync(new[] { RootFolderName, SheetsFolderName }, ct);
        var remoteSheets = await _drive.ListByKindAsync(BackupPlanner.SheetKind, ct);

        var cache = LoadHashCache();
        var plan = BackupPlanner.Plan(snapshot.Sheets, sheet => HashOf(sheet, cache), remoteSheets);
        SaveHashCache(cache, snapshot.Sheets);

        var usedNames = new HashSet<string>(
            remoteSheets.Where(r => !plan.RemoteOrphans.Contains(r)).Select(r => r.Name.ToLowerInvariant()));

        var done = 0;
        foreach (var upload in plan.Uploads)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress(BackupStage.UploadingSheets, done, plan.Uploads.Count));

            var name = upload.Existing?.Name ?? BackupPlanner.UniquePdfName(upload.Sheet.Title, usedNames);
            var properties = new Dictionary<string, string>
            {
                [BackupPlanner.KindProperty] = BackupPlanner.SheetKind,
                [BackupPlanner.FileProperty] = upload.Sheet.FileName,
                [BackupPlanner.HashProperty] = upload.Hash
            };

            await using var stream = File.OpenRead(SheetPath(upload.Sheet));
            await _drive.UploadAsync(name, sheetsFolderId, stream, "application/pdf", properties, upload.Existing?.Id, ct);
            done++;
        }

        progress?.Report(new BackupProgress(BackupStage.UploadingLibrary, 0, 0));

        // Sheets whose PDF is gone locally are not part of the snapshot either.
        var missingIds = snapshot.Sheets.Where(s => !File.Exists(SheetPath(s))).Select(s => s.Id).ToHashSet();
        var toSave = missingIds.Count > 0 ? FilterSheets(snapshot, missingIds) : snapshot;

        await UploadJsonAsync(SnapshotFileName, BackupPlanner.SnapshotKind, rootId, toSave, ct);

        var info = new BackupInfo
        {
            CreatedUtc = toSave.CreatedUtc,
            AppVersion = _options.AppVersion,
            FolderCount = toSave.Folders.Count,
            SheetCount = toSave.Sheets.Count,
            PdfCount = toSave.Sheets.Count,
            AnnotationCount = toSave.Annotations.Count,
            BookmarkCount = toSave.Bookmarks.Count
        };
        await UploadJsonAsync(InfoFileName, BackupPlanner.InfoKind, rootId, info, ct);

        var removed = 0;
        foreach (var orphan in plan.RemoteOrphans)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress(BackupStage.Cleaning, removed, plan.RemoteOrphans.Count));
            await _drive.DeleteAsync(orphan.Id, ct);
            removed++;
        }

        return new BackupResult(plan.Uploads.Count, plan.Unchanged, removed, plan.MissingLocal, DateTime.UtcNow);
    }

    private async Task UploadJsonAsync<T>(string name, string kind, string parentId, T value, CancellationToken ct)
    {
        var existing = (await _drive.ListByKindAsync(kind, ct)).FirstOrDefault();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await using var stream = new MemoryStream(bytes);
        var properties = new Dictionary<string, string> { [BackupPlanner.KindProperty] = kind };
        await _drive.UploadAsync(name, parentId, stream, "application/json", properties, existing?.Id, ct);
    }

    // -------------------------------------------------------------- snapshot

    public async Task<BackupSnapshot> BuildSnapshotAsync()
    {
        var connection = _database.Connection;
        return new BackupSnapshot
        {
            CreatedUtc = DateTime.UtcNow,
            AppVersion = _options.AppVersion,
            Folders = await connection.Table<Folder>().ToListAsync(),
            Sheets = await connection.Table<Sheet>().ToListAsync(),
            Annotations = await connection.Table<Annotation>().ToListAsync(),
            Bookmarks = await connection.Table<Bookmark>().ToListAsync(),
            PageCrops = await connection.Table<SheetPageCrop>().ToListAsync(),
            Preferences = new BackupPreferences
            {
                ReadingMode = _reading.GetReadingMode().ToString(),
                LanguageCode = _language.GetSavedLanguageCode(),
                SupportWithAds = _ads.GetSupportWithAdsEnabled()
            }
        };
    }

    public static string Serialize(BackupSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static BackupSnapshot Deserialize(string json)
    {
        BackupSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException("The backup file is damaged.", ex);
        }

        if (snapshot is null)
        {
            throw new BackupFormatException("The backup file is empty.");
        }

        if (snapshot.Version > BackupSnapshot.CurrentVersion)
        {
            throw new BackupFormatException("The backup was made by a newer version of the app. Update the app and try again.");
        }

        return snapshot;
    }

    private static BackupSnapshot FilterSheets(BackupSnapshot source, ISet<int> excludedSheetIds) => new()
    {
        Version = source.Version,
        CreatedUtc = source.CreatedUtc,
        AppVersion = source.AppVersion,
        Folders = source.Folders,
        Sheets = source.Sheets.Where(s => !excludedSheetIds.Contains(s.Id)).ToList(),
        Annotations = source.Annotations.Where(a => !excludedSheetIds.Contains(a.SheetId)).ToList(),
        Bookmarks = source.Bookmarks.Where(b => !excludedSheetIds.Contains(b.SheetId)).ToList(),
        PageCrops = source.PageCrops.Where(c => !excludedSheetIds.Contains(c.SheetId)).ToList(),
        Preferences = source.Preferences
    };

    // --------------------------------------------------------------- restore

    public async Task<RestoreResult> RestoreAsync(IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var staging = Path.Combine(DataDirectory, "RestoreStaging");
        RestoreResult result;
        try
        {
            result = await RestoreCoreAsync(staging, progress, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(staging);
            _gate.Release();
        }

        LibraryRestored?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private async Task<RestoreResult> RestoreCoreAsync(string staging, IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new BackupProgress(BackupStage.DownloadingLibrary, 0, 0));

        var snapshotFile = (await _drive.ListByKindAsync(BackupPlanner.SnapshotKind, ct))
            .OrderByDescending(f => f.ModifiedUtc ?? DateTime.MinValue)
            .FirstOrDefault() ?? throw new BackupNotFoundException();

        BackupSnapshot snapshot;
        using (var buffer = new MemoryStream())
        {
            try
            {
                await _drive.DownloadAsync(snapshotFile.Id, buffer, ct);
            }
            catch (DriveFileNotFoundException)
            {
                throw new BackupNotFoundException();
            }

            snapshot = Deserialize(Encoding.UTF8.GetString(buffer.ToArray()));
        }

        var remoteSheets = await _drive.ListByKindAsync(BackupPlanner.SheetKind, ct);
        var remoteByKey = new Dictionary<string, DriveFile>(StringComparer.Ordinal);
        foreach (var remote in remoteSheets)
        {
            if (remote.Properties.TryGetValue(BackupPlanner.FileProperty, out var key))
            {
                remoteByKey.TryAdd(key, remote);
            }
        }

        // Download everything into a staging folder first: if anything fails here
        // the current library has not been touched.
        TryDeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        var restorable = new List<Sheet>();
        var missing = new List<string>();
        var index = 0;
        foreach (var sheet in snapshot.Sheets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress(BackupStage.DownloadingSheets, index++, snapshot.Sheets.Count));

            if (!IsSafeFileName(sheet.FileName) || !remoteByKey.TryGetValue(sheet.FileName, out var file))
            {
                missing.Add(sheet.Title);
                continue;
            }

            var target = Path.Combine(staging, sheet.FileName);
            try
            {
                await using (var stream = File.Create(target))
                {
                    await _drive.DownloadAsync(file.Id, stream, ct);
                }

                restorable.Add(sheet);
            }
            catch (DriveFileNotFoundException)
            {
                File.Delete(target);
                missing.Add(sheet.Title);
            }
        }

        progress?.Report(new BackupProgress(BackupStage.SafetyCopy, 0, 0));
        var safetyPath = await CreateSafetyCopyAsync();

        progress?.Report(new BackupProgress(BackupStage.Applying, 0, 0));
        await ReplaceDatabaseAsync(snapshot, restorable);
        ReplaceSheetFiles(staging);

        var languageChanged = ApplyPreferences(snapshot.Preferences);

        return new RestoreResult(restorable.Count, missing, safetyPath, languageChanged);
    }

    private async Task ReplaceDatabaseAsync(BackupSnapshot snapshot, List<Sheet> restorableSheets)
    {
        // Rows are re-inserted (new auto-increment ids) and the references between
        // them are remapped; rows pointing at missing sheets are dropped. Runs in
        // one transaction on the shared connection, so no reopening is needed.
        await _database.Connection.RunInTransactionAsync(connection =>
        {
            connection.DeleteAll<Annotation>();
            connection.DeleteAll<Bookmark>();
            connection.DeleteAll<SheetPageCrop>();
            connection.DeleteAll<Sheet>();
            connection.DeleteAll<Folder>();

            var folderMap = new Dictionary<int, int>();
            foreach (var folder in snapshot.Folders)
            {
                var oldId = folder.Id;
                folder.Id = 0;
                connection.Insert(folder);
                folderMap[oldId] = folder.Id;
            }

            var sheetMap = new Dictionary<int, int>();
            foreach (var sheet in restorableSheets)
            {
                var oldId = sheet.Id;
                sheet.Id = 0;
                sheet.FolderId = sheet.FolderId is { } f && folderMap.TryGetValue(f, out var newFolder) ? newFolder : null;
                connection.Insert(sheet);
                sheetMap[oldId] = sheet.Id;
            }

            foreach (var annotation in snapshot.Annotations)
            {
                if (sheetMap.TryGetValue(annotation.SheetId, out var sheetId))
                {
                    annotation.Id = 0;
                    annotation.SheetId = sheetId;
                    connection.Insert(annotation);
                }
            }

            foreach (var bookmark in snapshot.Bookmarks)
            {
                if (sheetMap.TryGetValue(bookmark.SheetId, out var sheetId))
                {
                    bookmark.Id = 0;
                    bookmark.SheetId = sheetId;
                    connection.Insert(bookmark);
                }
            }

            foreach (var crop in snapshot.PageCrops)
            {
                if (sheetMap.TryGetValue(crop.SheetId, out var sheetId))
                {
                    crop.Id = 0;
                    crop.SheetId = sheetId;
                    connection.Insert(crop);
                }
            }
        });
    }

    private void ReplaceSheetFiles(string staging)
    {
        Directory.CreateDirectory(_storage.SheetsDirectory);
        foreach (var existing in Directory.GetFiles(_storage.SheetsDirectory))
        {
            File.Delete(existing);
        }

        foreach (var staged in Directory.GetFiles(staging))
        {
            File.Move(staged, Path.Combine(_storage.SheetsDirectory, Path.GetFileName(staged)));
        }
    }

    private bool ApplyPreferences(BackupPreferences preferences)
    {
        if (Enum.TryParse<ReadingMode>(preferences.ReadingMode, out var mode))
        {
            _reading.SetReadingMode(mode);
        }

        if (preferences.SupportWithAds is { } ads)
        {
            _ads.SetSupportWithAdsEnabled(ads);
        }

        var languageChanged = false;
        if (!string.IsNullOrWhiteSpace(preferences.LanguageCode))
        {
            languageChanged = !string.Equals(_language.GetSavedLanguageCode(), preferences.LanguageCode, StringComparison.Ordinal);
            _language.SaveLanguageCode(preferences.LanguageCode);
        }

        return languageChanged;
    }

    // Keeps the current library export and PDFs next to the app data until the
    // next restore, so a restore can be undone by hand.
    private async Task<string> CreateSafetyCopyAsync()
    {
        var root = Path.Combine(DataDirectory, "SafetyBackups");
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);

        var snapshot = await BuildSnapshotAsync();
        await File.WriteAllTextAsync(Path.Combine(folder, SnapshotFileName), Serialize(snapshot));

        if (Directory.Exists(_storage.SheetsDirectory))
        {
            var sheetsCopy = Path.Combine(folder, "Sheets");
            Directory.CreateDirectory(sheetsCopy);
            foreach (var file in Directory.GetFiles(_storage.SheetsDirectory))
            {
                File.Copy(file, Path.Combine(sheetsCopy, Path.GetFileName(file)), overwrite: true);
            }
        }

        foreach (var old in Directory.GetDirectories(root).Where(d => !string.Equals(d, folder, StringComparison.Ordinal)))
        {
            TryDeleteDirectory(old);
        }

        return folder;
    }

    // -------------------------------------------------------------- helpers

    private string SheetPath(Sheet sheet) => Path.Combine(_storage.SheetsDirectory, sheet.FileName);

    private static bool IsSafeFileName(string name) =>
        name.Length > 0 && name == Path.GetFileName(name) && name is not ("." or "..");

    private sealed class HashCacheEntry
    {
        public long Size { get; set; }

        public long Ticks { get; set; }

        public string Hash { get; set; } = string.Empty;
    }

    private Dictionary<string, HashCacheEntry> LoadHashCache()
    {
        try
        {
            if (!string.IsNullOrEmpty(_state.HashCacheJson))
            {
                return JsonSerializer.Deserialize<Dictionary<string, HashCacheEntry>>(_state.HashCacheJson) ?? new();
            }
        }
        catch (JsonException)
        {
        }

        return new();
    }

    private void SaveHashCache(Dictionary<string, HashCacheEntry> cache, IEnumerable<Sheet> sheets)
    {
        var live = sheets.Select(s => s.FileName).ToHashSet();
        foreach (var key in cache.Keys.Where(k => !live.Contains(k)).ToList())
        {
            cache.Remove(key);
        }

        _state.HashCacheJson = JsonSerializer.Serialize(cache);
    }

    private string? HashOf(Sheet sheet, Dictionary<string, HashCacheEntry> cache)
    {
        var info = new FileInfo(SheetPath(sheet));
        if (!info.Exists)
        {
            return null;
        }

        var ticks = info.LastWriteTimeUtc.Ticks;
        if (cache.TryGetValue(sheet.FileName, out var entry) && entry.Size == info.Length && entry.Ticks == ticks)
        {
            return entry.Hash;
        }

        using var stream = File.OpenRead(info.FullName);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        cache[sheet.FileName] = new HashCacheEntry { Size = info.Length, Ticks = ticks, Hash = hash };
        return hash;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
