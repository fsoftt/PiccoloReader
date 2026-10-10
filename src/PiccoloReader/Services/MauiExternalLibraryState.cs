using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Services;

public class MauiExternalLibraryState : IExternalLibraryState
{
    private const string LastSyncKey = "ExtLibLastSyncTicks";
    private const string HintDismissedKey = "ExtLibRecoveryHintDismissed";

    public DateTime? LastSyncUtc
    {
        get
        {
            var ticks = Preferences.Default.Get(LastSyncKey, 0L);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
        set => Preferences.Default.Set(LastSyncKey, value?.Ticks ?? 0L);
    }

    public string? SnapshotPath
    {
        get => Preferences.Default.Get<string?>("ExtLibSnapshotPath", null);
        set => Preferences.Default.Set("ExtLibSnapshotPath", value);
    }

    public bool RecoveryHintDismissed
    {
        get => Preferences.Default.Get(HintDismissedKey, false);
        set => Preferences.Default.Set(HintDismissedKey, value);
    }
}
