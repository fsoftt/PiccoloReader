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

    public void OpenAppSettings() => AppInfo.ShowSettingsUI();
}
