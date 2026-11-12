using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Web.Services;

// Library backup as a .zip: the PDFs laid out like the Android external folder (Folder/Title.pdf),
// the Core snapshot (piccolo-library.json: folders, sheets, annotations, bookmarks, crops) and the
// preferences. Import merges: sheets are matched by ContentHash and annotations/bookmarks by SyncId,
// reusing Core's LibraryRecovery.
public sealed class BackupService(AppDatabase db, IAppStorageProvider storage, BrowserInterop browser,
    PwaService pwa, PersistentDatabase persist, LibraryFiles files)
{
    private const string PreferencesEntry = "piccolo-settings.json";
    private const string TempZip = "/backup-import.zip";
    private static readonly Regex SnapshotName = new(@"^piccolo-library(?: \(\d+\))?\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // False when there is nothing to export.
    public async Task<bool> ExportAsync(IProgress<string> progress)
    {
        var sheets = await db.Connection.Table<Sheet>().ToListAsync();
        if (sheets.Count == 0) return false;

        progress.Report(WebText.Exporting(0, sheets.Count));
        await files.EnsureHashesAsync();
        sheets = await db.Connection.Table<Sheet>().ToListAsync();
        var folders = (await db.Connection.Table<Folder>().ToListAsync()).ToDictionary(f => f.Id, f => f.Name);

        var paths = new Dictionary<int, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sheets.Where(s => s.ExternalPath is not null)) { paths[s.Id] = s.ExternalPath!; taken.Add(s.ExternalPath!); }
        foreach (var s in sheets.Where(s => s.ExternalPath is null))
        {
            var folder = s.FolderId is { } id && folders.TryGetValue(id, out var n) ? n : null;
            var path = ExternalPaths.BuildSheetPath(folder, s.Title, taken);
            paths[s.Id] = path;
            taken.Add(path);
        }

        // BuildSnapshotAsync only reads the database; the store and state are not used.
        var snapshot = await new ExternalLibrarySync(db, storage, null!, null!).BuildSnapshotAsync();
        var pathByKey = sheets.ToDictionary(s => s.FileName, s => paths[s.Id]);
        foreach (var s in snapshot.Sheets) s.Path = pathByKey.GetValueOrDefault(s.SheetKey);

        await pwa.DownloadBeginAsync();
        using (var output = new JsDownloadStream(pwa))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(zip, ExternalPaths.SnapshotFileName, Encoding.UTF8.GetBytes(snapshot.ToJson()), CompressionLevel.Optimal);
            var prefs = JsonSerializer.Deserialize<Dictionary<string, string>>(await pwa.PreferencesJsonAsync()) ?? [];
            foreach (var k in prefs.Keys.Where(k => k.StartsWith("piccolo.linked.", StringComparison.Ordinal)).ToList()) prefs.Remove(k);
            await WriteEntryAsync(zip, PreferencesEntry, JsonSerializer.SerializeToUtf8Bytes(prefs), CompressionLevel.Optimal);

            for (var i = 0; i < sheets.Count; i++)
            {
                progress.Report(WebText.Exporting(i, sheets.Count));
                var bytes = await browser.ReadFileAsync("sheets/" + sheets[i].FileName);
                // PDFs are already compressed, so they are stored as they are.
                if (bytes is not null) await WriteEntryAsync(zip, paths[sheets[i].Id], bytes, CompressionLevel.NoCompression);
            }
            progress.Report(WebText.Exporting(sheets.Count, sheets.Count));
        }
        await pwa.DownloadEndAsync($"piccolo-library-{DateTime.Now:yyyyMMdd}.zip");
        return true;
    }

    // Returns null when the file is not a zip.
    public async Task<RecoveryResult?> ImportAsync(Stream zipContent, IProgress<string> progress)
    {
        progress.Report(WebText.Reading);
        // ZipArchive needs a seekable stream: stage the upload in the in-memory file system.
        await using (var temp = File.Create(TempZip))
        {
            var buffer = new byte[256 * 1024];
            int read, total = 0;
            while ((read = await zipContent.ReadAsync(buffer)) > 0)
            {
                await temp.WriteAsync(buffer.AsMemory(0, read));
                if (++total % 16 == 0) await Task.Delay(1);
            }
        }

        try
        {
            ZipArchive archive;
            try { archive = ZipFile.OpenRead(TempZip); }
            catch (InvalidDataException) { return null; }

            using (archive)
            {
                var store = new ZipLibraryStore(archive);
                await files.EnsureHashesAsync();
                await RemapSnapshotsAsync(archive, store);

                var result = await new LibraryRecovery(db, storage).RecoverAsync(
                    store, new Progress<SyncProgress>(p => progress.Report(WebText.Importing(p.Current, p.Total))));
                await files.AdoptStagedFilesAsync();
                await ApplyPreferencesAsync(store);
                await persist.FlushAsync();
                return result;
            }
        }
        finally
        {
            File.Delete(TempZip);
        }
    }

    // Sheets of the backup whose content already exists here are pointed at the existing sheet, so
    // recovery treats them as present (and still adds their annotations and bookmarks).
    private async Task RemapSnapshotsAsync(ZipArchive archive, ZipLibraryStore store)
    {
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in await db.Connection.Table<Sheet>().ToListAsync())
        {
            if (s.ContentHash is not null) existing.TryAdd(s.ContentHash, s.FileName);
        }

        foreach (var name in archive.Entries.Select(e => e.FullName).Where(n => SnapshotName.IsMatch(n)).ToList())
        {
            await using var stream = await store.OpenReadAsync(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var snapshot = LibrarySnapshot.TryParse(await reader.ReadToEndAsync());
            if (snapshot is null) continue;

            var keyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in snapshot.Sheets)
            {
                if (s.ContentHash is not null && existing.TryGetValue(s.ContentHash, out var key) && !string.Equals(key, s.SheetKey, StringComparison.OrdinalIgnoreCase))
                {
                    keyMap[s.SheetKey] = key;
                    s.SheetKey = key;
                }
            }
            string Map(string k) => keyMap.GetValueOrDefault(k, k);
            foreach (var a in snapshot.Annotations) a.SheetKey = Map(a.SheetKey);
            foreach (var b in snapshot.Bookmarks) b.SheetKey = Map(b.SheetKey);
            foreach (var c in snapshot.PageCrops) c.SheetKey = Map(c.SheetKey);
            store.Overrides[name] = Encoding.UTF8.GetBytes(snapshot.ToJson());
        }
    }

    // Keeps the preferences of this browser; the backup only fills in the ones not set yet.
    private async Task ApplyPreferencesAsync(ZipLibraryStore store)
    {
        await using var stream = await store.OpenReadAsync(PreferencesEntry);
        if (stream is null) return;
        try
        {
            var prefs = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream);
            foreach (var (key, value) in prefs ?? [])
            {
                if (key.StartsWith("piccolo.", StringComparison.Ordinal) && !key.StartsWith("piccolo.linked.", StringComparison.Ordinal)
                    && browser.GetLocalStorageSync(key) is null)
                {
                    browser.SetLocalStorageSync(key, value);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, byte[] data, CompressionLevel level)
    {
        var entry = zip.CreateEntry(path, level);
        await using var stream = await entry.OpenAsync();
        const int chunk = 512 * 1024;
        for (var offset = 0; offset < data.Length; offset += chunk)
        {
            await stream.WriteAsync(data.AsMemory(offset, Math.Min(chunk, data.Length - offset)));
            // Hand the thread back to the browser regularly so the progress text and taps stay live.
            if (offset > 0 && offset % (4 * chunk) == 0) await Task.Delay(1);
        }
        await Task.Delay(1);
    }
}

// Read-only IExternalLibraryStore over a zip (what LibraryRecovery reads), with replaceable entries.
public sealed class ZipLibraryStore(ZipArchive archive) : IExternalLibraryStore
{
    private readonly Dictionary<string, ZipArchiveEntry> _entries = archive.Entries
        .Where(e => !e.FullName.EndsWith('/') && e.Name.Length > 0)
        .ToDictionary(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> ListAsync() => Task.FromResult<IReadOnlyList<string>>(_entries.Keys.ToList());

    public Task<bool> ExistsAsync(string relativePath) => Task.FromResult(_entries.ContainsKey(relativePath));

    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (Overrides.TryGetValue(relativePath, out var bytes)) return new MemoryStream(bytes, writable: false);
        if (!_entries.TryGetValue(relativePath, out var entry)) return null;
        var ms = new MemoryStream();
        await using (var s = await entry.OpenAsync(cancellationToken))
        {
            await s.CopyToAsync(ms, cancellationToken);
        }
        ms.Position = 0;
        return ms;
    }

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string relativePath) => throw new NotSupportedException();

    public Task<bool> DeleteEmptyDirectoryAsync(string relativeDirectory) => throw new NotSupportedException();

    public Task<string> MoveAsync(string fromPath, string toPath) => throw new NotSupportedException();
}

// Write-only stream that hands the zip to the page in 1 MB chunks (collected as a Blob).
internal sealed class JsDownloadStream(PwaService pwa) : Stream
{
    private const int ChunkSize = 1024 * 1024;
    private readonly byte[] _buffer = new byte[ChunkSize];
    private int _count;
    private long _position;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _position;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _position += count;
        while (count > 0)
        {
            var n = Math.Min(count, ChunkSize - _count);
            Buffer.BlockCopy(buffer, offset, _buffer, _count, n);
            _count += n; offset += n; count -= n;
            if (_count == ChunkSize) Flush();
        }
    }

    public override void Flush()
    {
        if (_count == 0) return;
        pwa.DownloadChunk(_count == ChunkSize ? _buffer : _buffer[.._count]);
        _count = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Flush();
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
