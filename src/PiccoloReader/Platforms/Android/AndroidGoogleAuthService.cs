using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Platforms.Android;

// Google Identity Services AuthorizationClient for the drive.file scope. The
// OAuth client is matched by package name + signing SHA-1 in Google Cloud, so no
// client id lives in code. Silent calls return a token without UI once granted;
// otherwise Google hands back a PendingIntent that MainActivity forwards here.
public class AndroidGoogleAuthService : IGoogleAuthService
{
    public const int AuthorizationRequestCode = 9417;
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";

    // GoogleSignInStatusCodes.DEVELOPER_ERROR: the OAuth client / SHA-1 is not configured.
    private const int DeveloperError = 10;

    private static TaskCompletionSource<GoogleAuthResult>? s_pending;

    public bool IsSupported
    {
        get
        {
            try
            {
                return GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(global::Android.App.Application.Context) == ConnectionResult.Success;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public async Task<GoogleAuthResult> AuthorizeAsync(bool interactive, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new GoogleAuthResult(GoogleAuthStatus.Unavailable, Error: "Google Play services are not available.");
        }

        try
        {
            var activity = Platform.CurrentActivity;
            var client = activity is not null
                ? Identity.GetAuthorizationClient(activity)
                : Identity.GetAuthorizationClient(global::Android.App.Application.Context);

            var request = AuthorizationRequest.InvokeBuilder()
                .SetRequestedScopes(new List<Scope> { new Scope(DriveFileScope) })
                .Build();

            var result = (AuthorizationResult)await client.Authorize(request).AsAsync<Java.Lang.Object>();
            if (!result.HasResolution)
            {
                return ToResult(result);
            }

            if (!interactive || activity is null)
            {
                return new GoogleAuthResult(GoogleAuthStatus.RequiresInteraction, Error: "Google needs the user to grant access.");
            }

            var pending = new TaskCompletionSource<GoogleAuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            s_pending = pending;
            using var registration = cancellationToken.Register(() => pending.TrySetCanceled());

            await MainThread.InvokeOnMainThreadAsync(() =>
                activity.StartIntentSenderForResult(result.PendingIntent!.IntentSender, AuthorizationRequestCode, null, 0, 0, 0));

            return await pending.Task;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            return ex.StatusCode == DeveloperError
                ? new GoogleAuthResult(GoogleAuthStatus.Unavailable, Error: "Google sign-in is not configured for this build (OAuth client / SHA-1).")
                : new GoogleAuthResult(GoogleAuthStatus.Failed, Error: ex.Message);
        }
        catch (Exception ex)
        {
            return new GoogleAuthResult(GoogleAuthStatus.Failed, Error: ex.Message);
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            var client = Identity.GetAuthorizationClient(global::Android.App.Application.Context);
            var request = RevokeAccessRequest.InvokeBuilder()
                .SetScopes(new List<Scope> { new Scope(DriveFileScope) })
                .Build();
            await client.RevokeAccess(request).AsAsync<Java.Lang.Object>();
        }
        catch (Exception)
        {
            // Best effort: the local "connected" flag is what gates the feature.
        }
    }

    // Called from MainActivity.OnActivityResult. Returns true when the result was ours.
    public static bool HandleActivityResult(Activity activity, int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != AuthorizationRequestCode)
        {
            return false;
        }

        var pending = s_pending;
        s_pending = null;
        if (pending is null)
        {
            return true;
        }

        if (resultCode != Result.Ok || data is null)
        {
            pending.TrySetResult(new GoogleAuthResult(GoogleAuthStatus.Cancelled));
            return true;
        }

        try
        {
            var result = Identity.GetAuthorizationClient(activity).GetAuthorizationResultFromIntent(data);
            pending.TrySetResult(ToResult(result));
        }
        catch (Exception ex)
        {
            pending.TrySetResult(new GoogleAuthResult(GoogleAuthStatus.Failed, Error: ex.Message));
        }

        return true;
    }

    private static GoogleAuthResult ToResult(AuthorizationResult result) =>
        string.IsNullOrEmpty(result.AccessToken)
            ? new GoogleAuthResult(GoogleAuthStatus.Failed, Error: "Google did not return an access token.")
            : new GoogleAuthResult(GoogleAuthStatus.Success, result.AccessToken);
}
