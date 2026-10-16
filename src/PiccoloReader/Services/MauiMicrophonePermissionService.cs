using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

public class MauiMicrophonePermissionService : IMicrophonePermissionService
{
    public async Task<bool> EnsureGrantedAsync()
    {
        // Other platforms have no tuner backend; let the input report Unsupported.
        if (!PlatformInfo.IsAndroid)
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }

        return status == PermissionStatus.Granted;
    }

    public bool IsGranted()
    {
#if ANDROID
        return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
            global::Android.App.Application.Context, global::Android.Manifest.Permission.RecordAudio)
            == global::Android.Content.PM.Permission.Granted;
#else
        return true;
#endif
    }

    public void OpenAppSettings() => AppInfo.ShowSettingsUI();
}
