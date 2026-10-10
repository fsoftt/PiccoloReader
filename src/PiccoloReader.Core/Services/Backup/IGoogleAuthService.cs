namespace PiccoloReader.Core.Services.Backup;

public enum GoogleAuthStatus
{
    Success,
    RequiresInteraction,
    Cancelled,
    Unavailable,
    Failed
}

public sealed record GoogleAuthResult(GoogleAuthStatus Status, string? AccessToken = null, string? Error = null)
{
    public bool IsSuccess => Status == GoogleAuthStatus.Success && !string.IsNullOrEmpty(AccessToken);
}

// Platform sign-in for the drive.file scope (Android: Google Identity Services
// AuthorizationClient).
public interface IGoogleAuthService
{
    // False when the platform cannot do Google sign-in at all (iOS, no Play services).
    bool IsSupported { get; }

    // interactive: false must never show UI; it returns RequiresInteraction instead.
    Task<GoogleAuthResult> AuthorizeAsync(bool interactive, CancellationToken cancellationToken = default);

    // Best effort: revokes the app's grant on the platform side.
    Task SignOutAsync();
}
