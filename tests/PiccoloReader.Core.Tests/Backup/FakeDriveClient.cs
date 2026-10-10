using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Core.Tests.Backup;

// In-memory Drive: folders by path, files with properties and bytes.
public class FakeDriveClient : IDriveClient
{
    private int _nextId = 1;

    public sealed class Entry
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public Dictionary<string, string> Properties { get; set; } = new();
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime Modified { get; set; } = DateTime.UtcNow;
    }

    public Dictionary<string, Entry> Files { get; } = new();

    public Dictionary<string, string> Folders { get; } = new(); // path -> id

    public int UploadCalls { get; private set; }

    public int DownloadCalls { get; private set; }

    public Exception? FailDownloadsWith { get; set; }

    public Exception? FailListWith { get; set; }

    public string? Email { get; set; } = "user@example.com";

    public IEnumerable<Entry> OfKind(string kind) =>
        Files.Values.Where(f => f.Properties.GetValueOrDefault(BackupPlanner.KindProperty) == kind);

    public Task<string> EnsureFolderAsync(IReadOnlyList<string> path, CancellationToken cancellationToken = default)
    {
        if (FailListWith is not null)
        {
            throw FailListWith;
        }

        var key = string.Join("/", path);
        if (!Folders.TryGetValue(key, out var id))
        {
            id = "folder-" + _nextId++;
            Folders[key] = id;
        }

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<DriveFile>> ListByKindAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (FailListWith is not null)
        {
            throw FailListWith;
        }

        return Task.FromResult<IReadOnlyList<DriveFile>>(OfKind(kind).Select(ToDriveFile).ToList());
    }

    public async Task<DriveFile> UploadAsync(
        string name, string parentId, Stream content, string mimeType,
        IReadOnlyDictionary<string, string> properties, string? existingFileId, CancellationToken cancellationToken = default)
    {
        UploadCalls++;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        Entry entry;
        if (existingFileId is not null)
        {
            entry = Files[existingFileId];
            entry.Name = name;
        }
        else
        {
            entry = new Entry { Id = "file-" + _nextId++, Name = name, ParentId = parentId };
            Files[entry.Id] = entry;
        }

        entry.Content = buffer.ToArray();
        entry.Properties = new Dictionary<string, string>(properties);
        entry.Modified = DateTime.UtcNow;
        return ToDriveFile(entry);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        DownloadCalls++;
        if (FailDownloadsWith is not null)
        {
            throw FailDownloadsWith;
        }

        if (!Files.TryGetValue(fileId, out var entry))
        {
            throw new DriveFileNotFoundException(fileId);
        }

        await destination.WriteAsync(entry.Content, cancellationToken);
    }

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Files.Remove(fileId);
        return Task.CompletedTask;
    }

    public Task<string?> GetAccountEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult(Email);

    private static DriveFile ToDriveFile(Entry e) => new(e.Id, e.Name, e.Content.Length, e.Properties, e.Modified);
}

public class FakeBackupStateStore : IBackupStateStore
{
    public bool IsConnected { get; set; } = true;
    public string? AccountEmail { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public DateTime? LastBackupUtc { get; set; }
    public string? LastBackupError { get; set; }
    public bool NeedsSignIn { get; set; }
    public string? HashCacheJson { get; set; }
}

public class FakeAutoBackupScheduler : IAutoBackupScheduler
{
    public bool Scheduled { get; private set; }
    public void Schedule() => Scheduled = true;
    public void Cancel() => Scheduled = false;
}

public class FakeGoogleAuthService : IGoogleAuthService
{
    public bool IsSupported { get; set; } = true;
    public GoogleAuthResult Result { get; set; } = new(GoogleAuthStatus.Success, "token");
    public bool SignedOut { get; private set; }

    public Task<GoogleAuthResult> AuthorizeAsync(bool interactive, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);

    public Task SignOutAsync()
    {
        SignedOut = true;
        return Task.CompletedTask;
    }
}
