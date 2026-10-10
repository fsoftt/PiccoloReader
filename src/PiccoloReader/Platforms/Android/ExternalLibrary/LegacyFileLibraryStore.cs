using Android.Media;
using Stream = System.IO.Stream;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Platforms.Android.ExternalLibrary;

// Android 6-9: direct file IO in Documents/PiccoloReader, which needs the
// WRITE_EXTERNAL_STORAGE runtime permission (and files survive a reinstall
// readable, because the permission is granted again).
internal sealed class LegacyFileLibraryStore : IExternalLibraryStore
{
    private bool _requested;

    private static string Root => Path.Combine(
        global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)!.AbsolutePath,
        "PiccoloReader");

    public async Task<bool> EnsureAccessAsync(bool requestIfNeeded)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.StorageWrite>() == PermissionStatus.Granted)
            {
                return true;
            }

            if (!requestIfNeeded || _requested)
            {
                return false;
            }

            _requested = true;
            return await Permissions.RequestAsync<Permissions.StorageWrite>() == PermissionStatus.Granted;
        });
    }

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var path = Full(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var output = File.Create(path))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            MediaScannerConnection.ScanFile(global::Android.App.Application.Context, new[] { path }, new[] { mimeType }, null);
            return relativePath;
        }, cancellationToken);

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = Full(relativePath);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task<bool> ExistsAsync(string relativePath) => Task.FromResult(File.Exists(Full(relativePath)));

    public Task<bool> DeleteAsync(string relativePath)
    {
        var path = Full(relativePath);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        MediaScannerConnection.ScanFile(global::Android.App.Application.Context, new[] { path }, null, null);
        return Task.FromResult(true);
    }

    public Task<string> MoveAsync(string fromPath, string toPath)
    {
        var target = Full(toPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Full(fromPath), target);
        MediaScannerConnection.ScanFile(global::Android.App.Application.Context, new[] { Full(fromPath), target }, null, null);
        return Task.FromResult(toPath);
    }

    public Task<IReadOnlyList<string>> ListAsync()
    {
        var root = Root;
        IReadOnlyList<string> files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToList()
            : new List<string>();
        return Task.FromResult(files);
    }

    private static string Full(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
