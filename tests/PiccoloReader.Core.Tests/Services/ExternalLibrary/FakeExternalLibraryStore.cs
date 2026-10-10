using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Tests.Services.ExternalLibrary;

// In-memory stand-in for Documents/PiccoloReader (survives "reinstalls" because
// the tests keep it while replacing the app's database and private storage).
public class FakeExternalLibraryStore : IExternalLibraryStore
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public bool AccessGranted { get; set; } = true;

    public bool MoveSupported { get; set; } = true;

    public int WriteCount { get; private set; }

    public List<string> WrittenPaths { get; } = new();

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(AccessGranted);

    public virtual Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Files[relativePath] = buffer.ToArray();
        WriteCount++;
        WrittenPaths.Add(relativePath);
        return Task.FromResult(relativePath);
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(relativePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task<bool> ExistsAsync(string relativePath) => Task.FromResult(Files.ContainsKey(relativePath));

    public Task<bool> DeleteAsync(string relativePath) => Task.FromResult(Files.Remove(relativePath));

    public Task<string> MoveAsync(string fromPath, string toPath)
    {
        if (!MoveSupported)
        {
            throw new NotSupportedException();
        }

        Files[toPath] = Files[fromPath];
        Files.Remove(fromPath);
        return Task.FromResult(toPath);
    }

    public Task<IReadOnlyList<string>> ListAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());

    public string? SnapshotJson() =>
        Files.TryGetValue(ExternalPaths.SnapshotFileName, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;
}

public class FakeExternalLibraryState : IExternalLibraryState
{
    public DateTime? LastSyncUtc { get; set; }

    public string? SnapshotPath { get; set; }

    public bool RecoveryHintDismissed { get; set; }
}
