using Microsoft.JSInterop;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Web.Services;

public sealed record LinkedFolderInfo(string Name, string State);

// IExternalLibraryStore over a File System Access directory handle (wwwroot/js/linkedfolder.js).
public sealed class LinkedFolderStore(IJSRuntime js) : IExternalLibraryStore
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/linkedfolder.js").AsTask());

    private async Task<IJSObjectReference> ModuleAsync() => await _module.Value;

    public async Task<bool> SupportedAsync() => await (await ModuleAsync()).InvokeAsync<bool>("supported");

    public async Task<LinkedFolderInfo?> RestoreAsync() => await (await ModuleAsync()).InvokeAsync<LinkedFolderInfo?>("restore");

    public async Task<LinkedFolderInfo?> PickAsync() => await (await ModuleAsync()).InvokeAsync<LinkedFolderInfo?>("pick");

    public async Task<string> RequestAccessAsync() => await (await ModuleAsync()).InvokeAsync<string>("requestAccess");

    public async Task ForgetAsync() => await (await ModuleAsync()).InvokeVoidAsync("forget");

    public async Task<bool> EnsureAccessAsync(bool requestIfNeeded)
    {
        var m = await ModuleAsync();
        if (await m.InvokeAsync<string>("permission") == "granted") return true;
        return requestIfNeeded && await m.InvokeAsync<string>("requestAccess") == "granted";
    }

    public async Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        await (await ModuleAsync()).InvokeVoidAsync("write", cancellationToken, relativePath, ms.ToArray());
        return relativePath;
    }

    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var bytes = await (await ModuleAsync()).InvokeAsync<byte[]?>("read", cancellationToken, relativePath);
        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }

    public async Task<bool> ExistsAsync(string relativePath) => await (await ModuleAsync()).InvokeAsync<bool>("exists", relativePath);

    public async Task<bool> DeleteAsync(string relativePath) => await (await ModuleAsync()).InvokeAsync<bool>("remove", relativePath);

    public async Task<bool> DeleteEmptyDirectoryAsync(string relativeDirectory) =>
        await (await ModuleAsync()).InvokeAsync<bool>("removeEmptyDir", relativeDirectory);

    // Directory handles of user folders cannot be moved; ExternalLibrarySync falls back to write + delete.
    public Task<string> MoveAsync(string fromPath, string toPath) => throw new NotSupportedException();

    public async Task<IReadOnlyList<string>> ListAsync() => await (await ModuleAsync()).InvokeAsync<string[]>("list");
}

public sealed class LocalStorageExternalLibraryState(BrowserInterop b) : IExternalLibraryState
{
    public DateTime? LastSyncUtc
    {
        get => DateTime.TryParse(b.GetLocalStorageSync("piccolo.linked.lastSync"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        set => b.SetLocalStorageSync("piccolo.linked.lastSync", value?.ToString("O") ?? "");
    }

    public string? SnapshotPath
    {
        get => b.GetLocalStorageSync("piccolo.linked.snapshotPath") is { Length: > 0 } v ? v : null;
        set => b.SetLocalStorageSync("piccolo.linked.snapshotPath", value ?? "");
    }

    public bool RecoveryHintDismissed
    {
        get => b.GetLocalStorageSync("piccolo.linked.hintDismissed") == "1";
        set => b.SetLocalStorageSync("piccolo.linked.hintDismissed", value ? "1" : "0");
    }
}

// The linked folder: the web counterpart of Android's Documents/PiccoloReader external library.
// Mirrors PDFs and the snapshot with Core's ExternalLibrarySync (the snapshot also carries
// annotations and bookmarks) and recovers from the folder with Core's LibraryRecovery.
public sealed class LinkedFolderService
{
    private readonly AppDatabase _db;
    private readonly IAppStorageProvider _storage;
    private readonly BrowserInterop _browser;
    private readonly PersistentDatabase _persist;
    private readonly LibraryFiles _files;
    private readonly LinkedFolderStore _store;
    private readonly ExternalLibrarySync _sync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _debounce;

    public LinkedFolderService(AppDatabase db, IAppStorageProvider storage, BrowserInterop browser,
        PersistentDatabase persist, LibraryFiles files, LinkedFolderStore store, IExternalLibraryState state)
    {
        _db = db;
        _storage = storage;
        _browser = browser;
        _persist = persist;
        _files = files;
        _store = store;
        _sync = new ExternalLibrarySync(db, storage, store, state, TimeSpan.FromSeconds(2));
        persist.Changed += OnLibraryChanged;
    }

    public bool Supported { get; private set; }

    public LinkedFolderInfo? Linked { get; private set; }

    public bool Active => Linked is { State: "granted" };

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        Supported = await _store.SupportedAsync();
        if (!Supported) return;
        Linked = await _store.RestoreAsync();
        Changed?.Invoke();
    }

    // Must run from a user gesture (the folder picker).
    public async Task<(int Restored, bool Cancelled)> LinkAsync(IProgress<string> progress)
    {
        var info = await _store.PickAsync();
        if (info is null) return (0, true);
        Linked = info;
        Changed?.Invoke();
        // A folder that already holds a library (this app, or the Android one) is merged first.
        var result = await new LibraryRecovery(_db, _storage).RecoverAsync(_store);
        await _files.AdoptStagedFilesAsync();
        await SyncAllAsync(progress);
        return (result.SheetsRestored + result.OrphansImported, false);
    }

    // Must run from a user gesture (permission prompt).
    public async Task<bool> ReconnectAsync(IProgress<string> progress)
    {
        var state = await _store.RequestAccessAsync();
        Linked = Linked is null ? null : Linked with { State = state };
        Changed?.Invoke();
        if (state != "granted") return false;
        await SyncAllAsync(progress);
        return true;
    }

    public async Task UnlinkAsync()
    {
        _debounce?.Cancel();
        await _store.ForgetAsync();
        Linked = null;
        Changed?.Invoke();
    }

    // Copies what is missing from the folder and rewrites the snapshot.
    public async Task SyncAllAsync(IProgress<string>? progress = null)
    {
        if (!Active) return;
        await _gate.WaitAsync();
        try
        {
            var folders = (await _db.Connection.Table<Folder>().ToListAsync()).ToDictionary(f => f.Id, f => f.Name);
            var sheets = await _db.Connection.Table<Sheet>().ToListAsync();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sheets.Count; i++)
            {
                progress?.Report(WebText.LinkedFolderSyncing(i, sheets.Count));
                var sheet = sheets[i];
                if (!await IsInSyncAsync(sheet, folders, taken))
                {
                    await StageAndSyncAsync(sheet);
                }
                if (sheet.ExternalPath is not null) taken.Add(sheet.ExternalPath);
                await Task.Delay(1);
            }
            await _sync.WriteSnapshotNowAsync();
            _persist.ScheduleSave(notify: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Deletes the sheet's PDF from the folder (before the sheet row is removed).
    public async Task RemoveSheetAsync(Sheet sheet)
    {
        if (!Active) return;
        await _sync.RemoveSheetAsync(sheet);
    }

    public async Task RemoveFolderDirectoryAsync(string folderName)
    {
        if (!Active) return;
        await _sync.RemoveFolderDirectoryAsync(folderName);
    }

    private async Task<bool> IsInSyncAsync(Sheet sheet, Dictionary<int, string> folders, HashSet<string> taken)
    {
        if (sheet.ExternalPath is null || sheet.ContentHash is null) return false;
        var folderName = sheet.FolderId is { } id && folders.TryGetValue(id, out var n) ? n : null;
        var desired = ExternalPaths.BuildSheetPath(folderName, sheet.Title, taken);
        return string.Equals(desired, sheet.ExternalPath, StringComparison.Ordinal) && await _store.ExistsAsync(desired);
    }

    // ExternalLibrarySync reads the local PDF from the app storage path (the in-memory file system
    // here), while the web app keeps PDFs in OPFS: stage one copy for the duration of its sync.
    private async Task StageAndSyncAsync(Sheet sheet)
    {
        var path = Path.Combine(_storage.SheetsDirectory, sheet.FileName);
        var bytes = await _browser.ReadFileAsync("sheets/" + sheet.FileName);
        if (bytes is null) return;
        Directory.CreateDirectory(_storage.SheetsDirectory);
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await _sync.SyncSheetAsync(sheet, requestAccess: false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void OnLibraryChanged()
    {
        if (!Active) return;
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = RunDebouncedAsync(cts.Token);
    }

    private async Task RunDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(2500, token);
            await SyncAllAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Linked folder sync failed: {ex.Message}");
        }
    }
}
