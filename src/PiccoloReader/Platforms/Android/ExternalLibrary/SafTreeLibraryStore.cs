using Android.Content;
using Android.Provider;
using PiccoloReader.Core.Services.ExternalLibrary;
using AUri = Android.Net.Uri;

namespace PiccoloReader.Platforms.Android.ExternalLibrary;

// A folder the user granted through the system picker (ACTION_OPEN_DOCUMENT_TREE).
// This is how the app regains access to files a previous install created, and
// it keeps being used afterwards so new writes land in the very same folder.
internal sealed class SafTreeLibraryStore : IExternalLibraryStore
{
    private readonly AUri _treeUri;
    private readonly string _rootDocumentId;

    public SafTreeLibraryStore(AUri treeUri, string rootDocumentId)
    {
        _treeUri = treeUri;
        _rootDocumentId = rootDocumentId;
    }

    public AUri TreeUri => _treeUri;

    public string RootDocumentId => _rootDocumentId;

    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Task.FromResult(true);

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var segments = relativePath.Split('/');
            var parentId = _rootDocumentId;
            foreach (var directory in segments[..^1])
            {
                parentId = FindChildId(parentId, directory) ?? CreateDocument(parentId, DocumentsContract.Document.MimeTypeDir, directory);
            }

            var name = segments[^1];
            var fileId = FindChildId(parentId, name) ?? CreateDocument(parentId, mimeType, name);
            await using (var output = Resolver.OpenOutputStream(DocumentUri(fileId), "wt")
                ?? throw new IOException($"Cannot open {relativePath} for writing"))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            return relativePath;
        }, cancellationToken);

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.Run<Stream?>(() =>
        {
            var id = Resolve(relativePath);
            return id is null ? null : Resolver.OpenInputStream(DocumentUri(id));
        }, cancellationToken);

    public Task<bool> ExistsAsync(string relativePath) => Task.Run(() => Resolve(relativePath) is not null);

    public Task<bool> DeleteAsync(string relativePath) => Task.Run(() =>
    {
        var id = Resolve(relativePath);
        return id is not null && DocumentsContract.DeleteDocument(Resolver, DocumentUri(id));
    });

    public Task<bool> DeleteEmptyDirectoryAsync(string relativeDirectory) => Task.Run(() =>
    {
        try
        {
            var id = relativeDirectory.Length == 0 ? null : Resolve(relativeDirectory);
            return id is not null
                && id != _rootDocumentId
                && Children(id).Count == 0
                && DocumentsContract.DeleteDocument(Resolver, DocumentUri(id));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cannot remove empty directory {relativeDirectory}: {ex.Message}");
            return false;
        }
    });

    // Callers fall back to write + delete (the local cache is the source).
    public Task<string> MoveAsync(string fromPath, string toPath) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListAsync() => Task.Run<IReadOnlyList<string>>(() =>
    {
        var result = new List<string>();
        Walk(_rootDocumentId, string.Empty, result);
        return result;
    });

    private void Walk(string parentId, string prefix, List<string> result)
    {
        foreach (var child in Children(parentId))
        {
            if (child.MimeType == DocumentsContract.Document.MimeTypeDir)
            {
                Walk(child.Id, prefix + child.Name + "/", result);
            }
            else
            {
                result.Add(prefix + child.Name);
            }
        }
    }

    private string? Resolve(string relativePath)
    {
        var id = _rootDocumentId;
        foreach (var segment in relativePath.Split('/'))
        {
            var next = FindChildId(id, segment);
            if (next is null)
            {
                return null;
            }

            id = next;
        }

        return id;
    }

    private string? FindChildId(string parentId, string name) =>
        Children(parentId).Where(c => c.Name == name).Select(c => (string?)c.Id).FirstOrDefault();

    private List<(string Id, string Name, string MimeType)> Children(string parentId)
    {
        var result = new List<(string, string, string)>();
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(_treeUri, parentId)!;
        using var cursor = Resolver.Query(
            uri,
            new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType },
            null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            result.Add((cursor.GetString(0) ?? string.Empty, cursor.GetString(1) ?? string.Empty, cursor.GetString(2) ?? string.Empty));
        }

        return result;
    }

    private string CreateDocument(string parentId, string mimeType, string name)
    {
        var created = DocumentsContract.CreateDocument(Resolver, DocumentUri(parentId), mimeType, name)
            ?? throw new IOException($"Cannot create {name}");
        return DocumentsContract.GetDocumentId(created)!;
    }

    private AUri DocumentUri(string documentId) => DocumentsContract.BuildDocumentUriUsingTree(_treeUri, documentId)!;
}
