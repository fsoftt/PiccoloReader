using Android.Content;
using Android.Provider;
using PiccoloReader.Core.Services.ExternalLibrary;
using AUri = Android.Net.Uri;

namespace PiccoloReader.Platforms.Android.ExternalLibrary;

// Android 10+: Documents/PiccoloReader through MediaStore.Files. The app needs
// no permission for the files it creates itself; files left by a previous
// install are not visible here (that is what the SAF recovery is for).
internal sealed class MediaStoreLibraryStore : IExternalLibraryStore
{
    private const string RootDirectory = "Documents/PiccoloReader";

    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    private static AUri Collection => MediaStore.Files.GetContentUri("external")!;

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(true);

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var (directory, name) = Split(relativePath);
            var uri = Find(directory, name);
            if (uri is null)
            {
                var values = new ContentValues();
                values.Put(MediaStore.IMediaColumns.DisplayName, name);
                values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
                values.Put(MediaStore.IMediaColumns.RelativePath, RelativeDirectory(directory));
                uri = Resolver.Insert(Collection, values)
                    ?? throw new IOException($"MediaStore refused to create {relativePath}");
            }

            await using (var output = Resolver.OpenOutputStream(uri, "wt")
                ?? throw new IOException($"Cannot open {relativePath} for writing"))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            return ActualPath(uri) ?? relativePath;
        }, cancellationToken);

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.Run<Stream?>(() =>
        {
            var (directory, name) = Split(relativePath);
            var uri = Find(directory, name);
            return uri is null ? null : Resolver.OpenInputStream(uri);
        }, cancellationToken);

    public Task<bool> ExistsAsync(string relativePath) => Task.Run(() =>
    {
        var (directory, name) = Split(relativePath);
        return Find(directory, name) is not null;
    });

    public Task<bool> DeleteAsync(string relativePath) => Task.Run(() =>
    {
        var (directory, name) = Split(relativePath);
        var uri = Find(directory, name);
        return uri is not null && Resolver.Delete(uri, null, null) > 0;
    });


    // MediaStore has no directory rows: once the last file is gone the folder
    // can linger on disk. Remove it through the file system, only when empty.
    public Task<bool> DeleteEmptyDirectoryAsync(string relativeDirectory) => Task.Run(() =>
    {
        try
        {
            if (relativeDirectory.Length == 0)
            {
                return false;
            }

            var path = Path.Combine(
                global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)!.AbsolutePath,
                "PiccoloReader",
                relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }

            Directory.Delete(path, false);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cannot remove empty directory {relativeDirectory}: {ex.Message}");
            return false;
        }
    });
    public Task<string> MoveAsync(string fromPath, string toPath) => Task.Run(() =>
    {
        var (fromDirectory, fromName) = Split(fromPath);
        var (toDirectory, toName) = Split(toPath);
        var uri = Find(fromDirectory, fromName) ?? throw new FileNotFoundException(fromPath);
        if (Find(toDirectory, toName) is not null)
        {
            throw new IOException($"{toPath} already exists");
        }

        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, toName);
        values.Put(MediaStore.IMediaColumns.RelativePath, RelativeDirectory(toDirectory));
        if (Resolver.Update(uri, values, null, null) < 1)
        {
            throw new IOException($"Cannot move {fromPath}");
        }

        return ActualPath(uri) ?? toPath;
    });

    public Task<IReadOnlyList<string>> ListAsync() => Task.Run<IReadOnlyList<string>>(() =>
    {
        var result = new List<string>();
        var prefix = RootDirectory + "/";
        using var cursor = Resolver.Query(
            Collection,
            new[] { MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.RelativePath },
            $"{MediaStore.IMediaColumns.RelativePath} LIKE ?",
            new[] { prefix + "%" },
            null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var name = cursor.GetString(0);
            var directory = cursor.GetString(1) ?? prefix;
            if (name is null || !directory.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(directory[prefix.Length..] + name);
        }

        return result;
    });

    private static (string Directory, string Name) Split(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? (string.Empty, relativePath) : (relativePath[..index], relativePath[(index + 1)..]);
    }

    private static string RelativeDirectory(string directory) =>
        directory.Length == 0 ? RootDirectory + "/" : $"{RootDirectory}/{directory}/";

    private static AUri? Find(string directory, string name)
    {
        using var cursor = Resolver.Query(
            Collection,
            new[] { BaseColumns.Id },
            $"{MediaStore.IMediaColumns.RelativePath} = ? AND {MediaStore.IMediaColumns.DisplayName} = ?",
            new[] { RelativeDirectory(directory), name },
            null);
        return cursor is not null && cursor.MoveToFirst()
            ? ContentUris.WithAppendedId(Collection, cursor.GetLong(0))
            : null;
    }

    // The path MediaStore really used (it renames on conflicts with files it
    // does not let this app see).
    private static string? ActualPath(AUri uri)
    {
        using var cursor = Resolver.Query(
            uri,
            new[] { MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.RelativePath },
            null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var prefix = RootDirectory + "/";
        var directory = cursor.GetString(1) ?? prefix;
        return directory.StartsWith(prefix, StringComparison.Ordinal) ? directory[prefix.Length..] + cursor.GetString(0) : null;
    }
}
