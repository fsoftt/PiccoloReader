namespace PiccoloReader.Core.Services.Backup;

// Small persisted state of the Drive backup feature (Preferences on device).
public interface IBackupStateStore
{
    bool IsConnected { get; set; }

    string? AccountEmail { get; set; }

    bool AutoBackupEnabled { get; set; }

    DateTime? LastBackupUtc { get; set; }

    string? LastBackupError { get; set; }

    // The automatic backup could not authorize silently and needs the user to reconnect.
    bool NeedsSignIn { get; set; }

    // Local index of file content hashes (avoids re-hashing unchanged PDFs).
    string? HashCacheJson { get; set; }
}

public interface IAutoBackupScheduler
{
    void Schedule();

    void Cancel();
}

public sealed record BackupOptions(string AppVersion);

// Resolves a Drive access token silently; throws DriveAuthRequiredException when the
// user is not connected or Google needs interaction.
public class DriveTokenProvider
{
    private readonly IGoogleAuthService _auth;
    private readonly IBackupStateStore _state;

    public DriveTokenProvider(IGoogleAuthService auth, IBackupStateStore state)
    {
        _auth = auth;
        _state = state;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!_auth.IsSupported)
        {
            throw new DriveAuthRequiredException("Google sign-in is not available on this device.");
        }

        if (!_state.IsConnected)
        {
            throw new DriveAuthRequiredException("Not connected to Google Drive.");
        }

        var result = await _auth.AuthorizeAsync(interactive: false, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new DriveAuthRequiredException(result.Error ?? "Google sign-in is required.");
        }

        return result.AccessToken!;
    }
}
