namespace PiccoloReader.Core.Services.Tuner;

public interface IMicrophonePermissionService
{
    /// <summary>Returns true when the microphone permission is (or just got) granted; may show the system prompt.</summary>
    Task<bool> EnsureGrantedAsync();

    /// <summary>True when the permission is already granted. Never shows a prompt.</summary>
    bool IsGranted();

    /// <summary>Opens the system settings page of this app, for when the permission was denied permanently.</summary>
    void OpenAppSettings();
}
