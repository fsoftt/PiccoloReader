using Android.Content;
using Android.Provider;
using PiccoloReader.Core.Services.ExternalLibrary;
using AUri = Android.Net.Uri;

namespace PiccoloReader.Platforms.Android.ExternalLibrary;

// The store the app talks to. By default it writes Documents/PiccoloReader
// (MediaStore on Android 10+, plain files on 6-9). Once the user has granted a
// library folder through the picker (reinstall recovery) every operation goes
// through that tree instead, so the app keeps using the same folder.
public sealed class AndroidExternalLibraryStore : IExternalLibraryStore, IExternalLibraryPicker
{
    public const int PickTreeRequestCode = 9120;

    private const string TreeUriKey = "ExtLibTreeUri";
    private const string RootDocumentIdKey = "ExtLibRootDocId";

    private static TaskCompletionSource<Intent?>? s_pendingPick;

    private readonly object _lock = new();
    private IExternalLibraryStore? _backend;

    // Called by MainActivity.OnActivityResult.
    public static void OnPickResult(int requestCode, bool ok, Intent? data)
    {
        if (requestCode == PickTreeRequestCode)
        {
            var pending = s_pendingPick;
            s_pendingPick = null;
            pending?.TrySetResult(ok ? data : null);
        }
    }

    private IExternalLibraryStore Backend
    {
        get
        {
            lock (_lock)
            {
                return _backend ??= CreateBackend();
            }
        }
    }

    private static IExternalLibraryStore CreateBackend()
    {
        if (LoadSavedTree() is { } tree)
        {
            return tree;
        }

        return OperatingSystem.IsAndroidVersionAtLeast(29)
            ? new MediaStoreLibraryStore()
            : new LegacyFileLibraryStore();
    }

    public Task<bool> EnsureAccessAsync(bool requestIfNeeded) => Backend.EnsureAccessAsync(requestIfNeeded);

    public Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
        Backend.WriteAsync(relativePath, content, mimeType, cancellationToken);

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Backend.OpenReadAsync(relativePath, cancellationToken);

    public Task<bool> ExistsAsync(string relativePath) => Backend.ExistsAsync(relativePath);

    public Task<bool> DeleteAsync(string relativePath) => Backend.DeleteAsync(relativePath);

    public Task<string> MoveAsync(string fromPath, string toPath) => Backend.MoveAsync(fromPath, toPath);

    public Task<IReadOnlyList<string>> ListAsync() => Backend.ListAsync();

    public async Task<IExternalLibraryStore?> PickExistingLibraryAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            return null;
        }

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission
            | ActivityFlags.GrantWriteUriPermission
            | ActivityFlags.GrantPersistableUriPermission
            | ActivityFlags.GrantPrefixUriPermission);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            intent.PutExtra(
                DocumentsContract.ExtraInitialUri,
                DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", "primary:Documents/PiccoloReader"));
        }

        var completion = new TaskCompletionSource<Intent?>();
        s_pendingPick = completion;
        activity.StartActivityForResult(intent, PickTreeRequestCode);

        var data = await completion.Task;
        var treeUri = data?.Data;
        if (treeUri is null)
        {
            return null;
        }

        var resolver = global::Android.App.Application.Context.ContentResolver!;
        resolver.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);

        var rootId = await Task.Run(() => FindLibraryRoot(treeUri));
        Preferences.Default.Set(TreeUriKey, treeUri.ToString());
        Preferences.Default.Set(RootDocumentIdKey, rootId);

        var store = new SafTreeLibraryStore(treeUri, rootId);
        lock (_lock)
        {
            _backend = store;
        }

        return store;
    }

    // The user may pick Documents (containing PiccoloReader) or PiccoloReader
    // itself: descend into the PiccoloReader child unless the chosen folder is
    // already the library.
    private static string FindLibraryRoot(AUri treeUri)
    {
        var chosenId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        var probe = new SafTreeLibraryStore(treeUri, chosenId);
        var files = probe.ListAsync().GetAwaiter().GetResult();
        if (files.Contains(ExternalPaths.SnapshotFileName))
        {
            return chosenId;
        }

        var resolver = global::Android.App.Application.Context.ContentResolver!;
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, chosenId)!;
        using var cursor = resolver.Query(
            children,
            new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType },
            null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            if (cursor.GetString(1) == "PiccoloReader" && cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
            {
                return cursor.GetString(0)!;
            }
        }

        return chosenId;
    }

    private static SafTreeLibraryStore? LoadSavedTree()
    {
        var uriText = Preferences.Default.Get<string?>(TreeUriKey, null);
        var rootId = Preferences.Default.Get<string?>(RootDocumentIdKey, null);
        if (string.IsNullOrEmpty(uriText) || string.IsNullOrEmpty(rootId))
        {
            return null;
        }

        var uri = AUri.Parse(uriText);
        var resolver = global::Android.App.Application.Context.ContentResolver!;
        var stillGranted = resolver.PersistedUriPermissions
            .Any(p => p.IsWritePermission && p.Uri is not null && p.Uri.Equals(uri));
        return stillGranted && uri is not null ? new SafTreeLibraryStore(uri, rootId) : null;
    }
}
