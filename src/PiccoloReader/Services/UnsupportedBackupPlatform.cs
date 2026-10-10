#if !ANDROID
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Services;

// Drive backup is Android-only for now; other platforms get inert stand-ins.
public class UnsupportedGoogleAuthService : IGoogleAuthService
{
    public bool IsSupported => false;

    public Task<GoogleAuthResult> AuthorizeAsync(bool interactive, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GoogleAuthResult(GoogleAuthStatus.Unavailable, Error: "Not supported on this platform."));

    public Task SignOutAsync() => Task.CompletedTask;
}

public class NoOpAutoBackupScheduler : IAutoBackupScheduler
{
    public void Schedule()
    {
    }

    public void Cancel()
    {
    }
}
#endif
