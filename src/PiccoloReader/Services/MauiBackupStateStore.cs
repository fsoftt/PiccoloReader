using System.Globalization;
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Services;

public class MauiBackupStateStore : IBackupStateStore
{
    private const string ConnectedKey = "DriveBackup.Connected";
    private const string EmailKey = "DriveBackup.Email";
    private const string AutoKey = "DriveBackup.Auto";
    private const string LastKey = "DriveBackup.LastUtc";
    private const string ErrorKey = "DriveBackup.LastError";
    private const string NeedsSignInKey = "DriveBackup.NeedsSignIn";
    private const string HashCacheKey = "DriveBackup.HashCache";

    public bool IsConnected
    {
        get => Preferences.Default.Get(ConnectedKey, false);
        set => Preferences.Default.Set(ConnectedKey, value);
    }

    public string? AccountEmail
    {
        get => Preferences.Default.Get(EmailKey, (string?)null);
        set => SetOrRemove(EmailKey, value);
    }

    public bool AutoBackupEnabled
    {
        get => Preferences.Default.Get(AutoKey, false);
        set => Preferences.Default.Set(AutoKey, value);
    }

    public DateTime? LastBackupUtc
    {
        get => long.TryParse(Preferences.Default.Get(LastKey, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
        set => SetOrRemove(LastKey, value?.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    public string? LastBackupError
    {
        get => Preferences.Default.Get(ErrorKey, (string?)null);
        set => SetOrRemove(ErrorKey, value);
    }

    public bool NeedsSignIn
    {
        get => Preferences.Default.Get(NeedsSignInKey, false);
        set => Preferences.Default.Set(NeedsSignInKey, value);
    }

    public string? HashCacheJson
    {
        get => Preferences.Default.Get(HashCacheKey, (string?)null);
        set => SetOrRemove(HashCacheKey, value);
    }

    private static void SetOrRemove(string key, string? value)
    {
        if (value is null)
        {
            Preferences.Default.Remove(key);
        }
        else
        {
            Preferences.Default.Set(key, value);
        }
    }
}
