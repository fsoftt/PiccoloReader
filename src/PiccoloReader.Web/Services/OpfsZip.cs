using System.IO.Compression;
using System.Text;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Web.Services;

// Minimal zip reader over a file in OPFS: the central directory is parsed once, each entry is then
// read by byte range (stored or deflated). No zip64 (backups are far below 4 GB). Only one entry is
// ever held in memory.
public sealed class OpfsZip
{
    public sealed record Entry(string FullName, int Method, long CompressedSize, long Size, long HeaderOffset);

    private readonly BrowserInterop _browser;
    private readonly string _name;

    private OpfsZip(BrowserInterop browser, string name, List<Entry> entries)
    {
        _browser = browser;
        _name = name;
        Entries = entries;
    }

    public IReadOnlyList<Entry> Entries { get; }

    // Null when the file is not a (supported) zip.
    public static async Task<OpfsZip?> OpenAsync(BrowserInterop browser, string name)
    {
        var size = await browser.FileSizeAsync(name);
        if (size < 22) return null;
        var tailLength = Math.Min(size, 22 + 65535);
        var tail = await browser.ReadRangeAsync(name, size - tailLength, tailLength);
        var eocd = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 5 && tail[i + 3] == 6) { eocd = i; break; }
        }
        if (eocd < 0) return null;

        var count = BitConverter.ToUInt16(tail, eocd + 10);
        var cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        var cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        if (cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF || count == 0xFFFF) return null; // zip64
        if (cdOffset + (long)cdSize > size) return null;

        var cd = await browser.ReadRangeAsync(name, cdOffset, cdSize);
        var entries = new List<Entry>(count);
        var p = 0;
        while (p + 46 <= cd.Length && BitConverter.ToUInt32(cd, p) == 0x02014b50)
        {
            var flags = BitConverter.ToUInt16(cd, p + 8);
            var method = BitConverter.ToUInt16(cd, p + 10);
            var comp = BitConverter.ToUInt32(cd, p + 20);
            var len = BitConverter.ToUInt32(cd, p + 24);
            var nameLen = BitConverter.ToUInt16(cd, p + 28);
            var extraLen = BitConverter.ToUInt16(cd, p + 30);
            var commentLen = BitConverter.ToUInt16(cd, p + 32);
            var offset = BitConverter.ToUInt32(cd, p + 42);
            var encoding = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
            entries.Add(new Entry(encoding.GetString(cd, p + 46, nameLen), method, comp, len, offset));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return new OpfsZip(browser, name, entries);
    }

    public async Task<byte[]> ReadAsync(Entry entry)
    {
        var header = await _browser.ReadRangeAsync(_name, entry.HeaderOffset, 30);
        var start = entry.HeaderOffset + 30 + BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
        var data = entry.CompressedSize == 0 ? [] : await _browser.ReadRangeAsync(_name, start, entry.CompressedSize);
        if (entry.Method == 0) return data;
        if (entry.Method != 8) throw new NotSupportedException($"Zip method {entry.Method} is not supported.");
        await using var input = new MemoryStream(data, writable: false);
        await using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        var output = new MemoryStream((int)Math.Min(entry.Size, int.MaxValue));
        await deflate.CopyToAsync(output);
        return output.ToArray();
    }
}

// Read-only IExternalLibraryStore over a zip in OPFS (what LibraryRecovery reads), with replaceable entries.
public sealed class ZipLibraryStore(OpfsZip archive) : IExternalLibraryStore
{
    private readonly Dictionary<string, OpfsZip.Entry> _entries = archive.Entries
        .Where(e => !e.FullName.EndsWith('/'))
        .GroupBy(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Runs before every entry is opened (used to move finished PDFs out of memory).
    public Func<Task>? BeforeOpen { get; set; }

    public IEnumerable<string> Names => _entries.Keys;

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> ListAsync() => Task.FromResult<IReadOnlyList<string>>(_entries.Keys.ToList());

    public Task<bool> ExistsAsync(string relativePath) => Task.FromResult(_entries.ContainsKey(relativePath));

    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (Overrides.TryGetValue(relativePath, out var bytes)) return new MemoryStream(bytes, writable: false);
        if (!_entries.TryGetValue(relativePath, out var entry)) return null;
        if (BeforeOpen is not null) await BeforeOpen();
        return new MemoryStream(await archive.ReadAsync(entry), writable: false);
    }

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string relativePath) => throw new NotSupportedException();

    public Task<bool> DeleteEmptyDirectoryAsync(string relativeDirectory) => throw new NotSupportedException();

    public Task<string> MoveAsync(string fromPath, string toPath) => throw new NotSupportedException();
}
