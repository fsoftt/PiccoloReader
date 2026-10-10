namespace PiccoloReader.Core.Services.ExternalLibrary;

// The durable, user-visible copy of the library: Documents/PiccoloReader on
// Android. Paths are relative to that folder and use '/' separators
// ("Folder/Title.pdf", "piccolo-library.json"). Implementations are
// platform-specific (MediaStore, direct file IO or a SAF tree after recovery).
public interface IExternalLibraryStore
{
    // Makes sure the store can be written (runtime permission on Android 6-9).
    // Returns false when it cannot, in which case callers retry later.
    // requestIfNeeded: may show the permission prompt (never from background work).
    Task<bool> EnsureAccessAsync(bool requestIfNeeded);

    // Creates or overwrites a file and returns the path actually used (the
    // platform may rename on a name conflict with a file the app does not own).
    Task<string> WriteAsync(string relativePath, Stream content, string mimeType, CancellationToken cancellationToken = default);

    // Null when the file does not exist or is not readable.
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string relativePath);

    // True when something was deleted.
    Task<bool> DeleteAsync(string relativePath);

    // Moves a file and returns its new path. May throw NotSupportedException
    // (or any IO failure); callers fall back to write + delete.
    Task<string> MoveAsync(string fromPath, string toPath);

    // Every file visible to the store, as relative paths.
    Task<IReadOnlyList<string>> ListAsync();
}

// Lets the user choose an existing library folder (Android SAF tree picker).
public interface IExternalLibraryPicker
{
    // Null when cancelled. The returned store is also persisted as the one the
    // app keeps writing to from now on.
    Task<IExternalLibraryStore?> PickExistingLibraryAsync();
}

// Small persisted state (preferences) of the external library feature.
public interface IExternalLibraryState
{
    DateTime? LastSyncUtc { get; set; }

    // The user closed the "recover your library" hint on the empty library.
    bool RecoveryHintDismissed { get; set; }
}

// Anything that changes library data calls this so the snapshot is rewritten.
public interface ILibraryChangeNotifier
{
    void NotifyChanged();
}
