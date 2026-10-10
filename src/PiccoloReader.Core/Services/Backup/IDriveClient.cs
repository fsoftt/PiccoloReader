namespace PiccoloReader.Core.Services.Backup;

public sealed record DriveFile(
    string Id,
    string Name,
    long? Size,
    IReadOnlyDictionary<string, string> Properties,
    DateTime? ModifiedUtc);

// Minimal Google Drive surface the backup logic needs. The production
// implementation is GoogleDriveClient (Drive REST v3); tests use a fake.
public interface IDriveClient
{
    // Finds or creates the nested visible folders (e.g. PiccoloReader/Partituras)
    // and returns the id of the last one.
    Task<string> EnsureFolderAsync(IReadOnlyList<string> path, CancellationToken cancellationToken = default);

    // Non-trashed files created by the app whose appProperties[piccoloKind] == kind,
    // wherever the user may have moved them.
    Task<IReadOnlyList<DriveFile>> ListByKindAsync(string kind, CancellationToken cancellationToken = default);

    // Creates the file in parentId, or replaces name/content/properties of
    // existingFileId (location untouched) when given.
    Task<DriveFile> UploadAsync(
        string name,
        string parentId,
        Stream content,
        string mimeType,
        IReadOnlyDictionary<string, string> properties,
        string? existingFileId,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default);

    Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);

    Task<string?> GetAccountEmailAsync(CancellationToken cancellationToken = default);
}

public class DriveException : Exception
{
    public DriveException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

// Sign-in is missing, expired or revoked: the user has to connect again.
public class DriveAuthRequiredException : DriveException
{
    public DriveAuthRequiredException(string message) : base(message)
    {
    }
}

public class DriveFileNotFoundException : DriveException
{
    public DriveFileNotFoundException(string message) : base(message)
    {
    }
}

public class DriveQuotaException : DriveException
{
    public DriveQuotaException(string message) : base(message)
    {
    }
}
