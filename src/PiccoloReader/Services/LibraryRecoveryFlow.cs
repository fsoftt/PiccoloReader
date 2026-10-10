using System.Globalization;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Services;

// UI flows around the external library folder, shared by the library and
// settings pages.
public class LibraryRecoveryFlow
{
    private readonly IExternalLibraryPicker _picker;
    private readonly LibraryRecovery _recovery;
    private readonly ExternalLibrarySync _sync;
    private readonly IExternalLibraryState _state;
    private int _syncing;

    public LibraryRecoveryFlow(
        IExternalLibraryPicker picker,
        LibraryRecovery recovery,
        ExternalLibrarySync sync,
        IExternalLibraryState state)
    {
        _picker = picker;
        _recovery = recovery;
        _sync = sync;
        _state = state;
    }

    public IExternalLibraryState State => _state;

    public ExternalLibrarySync Sync => _sync;

    // Explains, lets the user pick the folder, restores and reports.
    // True when the library may have changed.
    public async Task<bool> RunRecoveryAsync(Page host)
    {
        var proceed = await host.DisplayAlertAsync(
            AppStrings.RecoverLibraryIntroTitle,
            AppStrings.RecoverLibraryIntroMessage,
            AppStrings.RecoverChooseFolder,
            AppStrings.Cancel);
        if (!proceed)
        {
            return false;
        }

        try
        {
            var store = await _picker.PickExistingLibraryAsync();
            if (store is null)
            {
                return false;
            }

            var result = await Task.Run(() => _recovery.RecoverAsync(store));
            if (!result.FoundAnything)
            {
                await host.DisplayAlertAsync(AppStrings.RecoverLibraryIntroTitle, AppStrings.RecoverNothingFound, AppStrings.OK);
                return false;
            }

            _state.RecoveryHintDismissed = true;
            _state.SnapshotPath = null; // the recovered folder holds the canonical file

            // Writes whatever the restored library still lacks (and the snapshot).
            await Task.Run(() => _sync.SyncAllAsync(requestAccess: true));

            var message = AppStrings.RecoverResult(
                result.SheetsRestored + result.OrphansImported,
                result.FoldersAdded,
                result.AnnotationsAdded,
                result.BookmarksAdded,
                result.CropsAdded);
            if (result.MissingSheets.Count > 0)
            {
                message += "\n" + string.Format(CultureInfo.CurrentUICulture, AppStrings.RecoverMissingFormat, result.MissingSheets.Count);
            }

            await host.DisplayAlertAsync(AppStrings.RecoverLibraryIntroTitle, message, AppStrings.OK);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library recovery failed: {ex}");
            await host.DisplayAlertAsync(AppStrings.RecoverLibraryIntroTitle, AppStrings.RecoverFailedMessage, AppStrings.OK);
            return true;
        }
    }

    // Background migration / retry: copies sheets the external folder lacks
    // (1.1.0 libraries, or an earlier failure) and rewrites the snapshot.
    // onStatus receives progress text, then null when finished. Returns true
    // when it changed anything worth refreshing.
    public async Task<bool> SyncPendingAsync(Action<string?> onStatus)
    {
        if (Interlocked.Exchange(ref _syncing, 1) == 1)
        {
            return false;
        }

        try
        {
            var pending = await _sync.CountPendingAsync();
            if (pending == 0 && _state.LastSyncUtc is not null && await _sync.CountUnknownPageCountAsync() == 0)
            {
                return false;
            }

            var progress = new Progress<SyncProgress>(p =>
            {
                if (pending > 0)
                {
                    onStatus(string.Format(CultureInfo.CurrentUICulture, AppStrings.StorageSyncProgressFormat, p.Current, p.Total));
                }
            });
            var result = await Task.Run(() => _sync.SyncAllAsync(progress, requestAccess: true));

            if (pending > 0 && result.Failed == 0 && !result.AccessDenied)
            {
                onStatus(AppStrings.StorageSyncDone);
                await Task.Delay(2500);
            }
            else if (pending > 0)
            {
                onStatus(AppStrings.StorageSyncPending);
                await Task.Delay(3500);
            }

            return pending > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Background sync failed: {ex}");
            return false;
        }
        finally
        {
            onStatus(null);
            Interlocked.Exchange(ref _syncing, 0);
        }
    }
}
