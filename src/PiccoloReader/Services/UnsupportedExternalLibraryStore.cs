using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Services;

// Platforms without a shared documents folder (iOS): nothing is mirrored.
public class UnsupportedExternalLibraryStore : IExternalLibraryStore, IExternalLibraryPicker
{
    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(false);

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);

    public Task<bool> ExistsAsync(string relativePath) => Task.FromResult(false);

    public Task<bool> DeleteAsync(string relativePath) => Task.FromResult(false);

    public Task<string> MoveAsync(string fromPath, string toPath) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<IExternalLibraryStore?> PickExistingLibraryAsync() => Task.FromResult<IExternalLibraryStore?>(null);
}
