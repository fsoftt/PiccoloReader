using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Core.ViewModels;

public sealed record BackupUiMessage(string Title, string Text);

// State and actions of the "Backup" section in Settings. Methods return the
// message the page should show (or null for silent success); dialogs live in the page.
public partial class BackupSettingsViewModel : ObservableObject
{
    private readonly BackupService _backup;
    private readonly IGoogleAuthService _auth;
    private readonly IDriveClient _drive;
    private readonly IBackupStateStore _state;
    private readonly IAutoBackupScheduler _scheduler;
    private bool _refreshing;

    public BackupSettingsViewModel(
        BackupService backup,
        IGoogleAuthService auth,
        IDriveClient drive,
        IBackupStateStore state,
        IAutoBackupScheduler scheduler)
    {
        _backup = backup;
        _auth = auth;
        _drive = drive;
        _state = state;
        _scheduler = scheduler;
        Refresh();
    }

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private string _accountText = string.Empty;

    [ObservableProperty]
    private string _connectActionText = string.Empty;

    [ObservableProperty]
    private string _lastBackupText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _autoBackupEnabled;

    [ObservableProperty]
    private bool _canUseBackup;

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            IsConnected = _state.IsConnected;
            AutoBackupEnabled = _state.AutoBackupEnabled && _state.IsConnected;
            AccountText = _state.IsConnected
                ? _state.AccountEmail ?? AppStrings.BackupAccountDescription
                : AppStrings.BackupNotConnected;
            ConnectActionText = _state.IsConnected ? AppStrings.BackupDisconnect : AppStrings.BackupConnect;
            CanUseBackup = _state.IsConnected && IsIdle;
            LastBackupText = BuildLastBackupText();
        }
        finally
        {
            _refreshing = false;
        }
    }

    partial void OnAutoBackupEnabledChanged(bool value)
    {
        if (_refreshing)
        {
            return;
        }

        _state.AutoBackupEnabled = value;
        if (value)
        {
            _scheduler.Schedule();
        }
        else
        {
            _scheduler.Cancel();
        }
    }

    partial void OnIsIdleChanged(bool value) => CanUseBackup = _state.IsConnected && value;

    private string BuildLastBackupText()
    {
        if (_state.NeedsSignIn && _state.AutoBackupEnabled)
        {
            return AppStrings.AutoBackupNeedsSignIn;
        }

        if (_state.LastBackupUtc is { } last)
        {
            var when = last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var text = string.Format(CultureInfo.CurrentCulture, AppStrings.LastBackupFormat, when);
            if (!string.IsNullOrEmpty(_state.LastBackupError))
            {
                text += "\n" + string.Format(CultureInfo.CurrentCulture, AppStrings.LastBackupFailedFormat, _state.LastBackupError);
            }

            return text;
        }

        return AppStrings.LastBackupNever;
    }

    public async Task<BackupUiMessage?> ConnectAsync()
    {
        if (!_auth.IsSupported)
        {
            return new BackupUiMessage(AppStrings.BackupFailedTitle, AppStrings.BackupUnavailableMessage);
        }

        SetBusy(AppStrings.BackupProgressPreparing);
        try
        {
            var result = await _auth.AuthorizeAsync(interactive: true);
            if (!result.IsSuccess)
            {
                return result.Status switch
                {
                    GoogleAuthStatus.Cancelled => null,
                    GoogleAuthStatus.Unavailable => new BackupUiMessage(AppStrings.BackupFailedTitle, AppStrings.BackupUnavailableMessage),
                    _ => new BackupUiMessage(AppStrings.BackupFailedTitle, string.Format(CultureInfo.CurrentCulture, AppStrings.BackupGenericFailureFormat, result.Error ?? result.Status.ToString()))
                };
            }

            _state.IsConnected = true;
            _state.NeedsSignIn = false;
            try
            {
                _state.AccountEmail = await _drive.GetAccountEmailAsync();
            }
            catch (DriveException)
            {
                // The email is cosmetic; being connected is what matters.
            }

            return null;
        }
        finally
        {
            SetIdle();
        }
    }

    public async Task DisconnectAsync()
    {
        SetBusy(string.Empty);
        try
        {
            _scheduler.Cancel();
            await _auth.SignOutAsync();
            _state.IsConnected = false;
            _state.AutoBackupEnabled = false;
            _state.AccountEmail = null;
            _state.NeedsSignIn = false;
        }
        finally
        {
            SetIdle();
        }
    }

    public async Task<BackupUiMessage?> BackupNowAsync()
    {
        SetBusy(AppStrings.BackupProgressPreparing);
        try
        {
            var progress = new Progress<BackupProgress>(p => StatusText = Describe(p));
            var result = await WithReauthAsync(() => _backup.BackupAsync(progress));

            var text = string.Format(CultureInfo.CurrentCulture, AppStrings.BackupCompleteFormat, result.Uploaded, result.Unchanged);
            if (result.MissingLocalFiles.Count > 0)
            {
                text += "\n" + string.Format(CultureInfo.CurrentCulture, AppStrings.BackupCompleteMissingFormat,
                    result.MissingLocalFiles.Count, string.Join(", ", result.MissingLocalFiles));
            }

            return new BackupUiMessage(AppStrings.BackupCompleteTitle, text);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new BackupUiMessage(AppStrings.BackupFailedTitle, DescribeError(ex));
        }
        finally
        {
            SetIdle();
        }
    }

    public async Task<BackupUiMessage?> RestoreAsync()
    {
        SetBusy(AppStrings.BackupProgressDownloadingLibrary);
        try
        {
            var progress = new Progress<BackupProgress>(p => StatusText = Describe(p));
            var result = await WithReauthAsync(() => _backup.RestoreAsync(progress));

            var text = string.Format(CultureInfo.CurrentCulture, AppStrings.RestoreCompleteFormat, result.RestoredSheets);
            if (result.MissingSheets.Count > 0)
            {
                text += "\n" + string.Format(CultureInfo.CurrentCulture, AppStrings.RestoreMissingFormat,
                    result.MissingSheets.Count, string.Join(", ", result.MissingSheets));
            }

            if (result.LanguageChanged)
            {
                text += "\n" + AppStrings.RestoreLanguageNote;
            }

            return new BackupUiMessage(AppStrings.RestoreCompleteTitle, text);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new BackupUiMessage(AppStrings.BackupFailedTitle, DescribeError(ex));
        }
        finally
        {
            SetIdle();
        }
    }

    // A silent token can be refused (grant revoked, token expired): ask Google
    // once, interactively, and retry.
    private async Task<T> WithReauthAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DriveAuthRequiredException) when (_auth.IsSupported)
        {
            var result = await _auth.AuthorizeAsync(interactive: true);
            if (!result.IsSuccess)
            {
                throw new DriveAuthRequiredException(result.Error ?? AppStrings.BackupAuthRequiredMessage);
            }

            _state.IsConnected = true;
            return await action();
        }
    }

    private static string Describe(BackupProgress p) => p.Stage switch
    {
        BackupStage.UploadingSheets => string.Format(CultureInfo.CurrentCulture, AppStrings.BackupProgressUploadingFormat, Math.Min(p.Current + 1, p.Total), p.Total),
        BackupStage.UploadingLibrary => AppStrings.BackupProgressUploadingLibrary,
        BackupStage.Cleaning => AppStrings.BackupProgressCleaning,
        BackupStage.DownloadingLibrary => AppStrings.BackupProgressDownloadingLibrary,
        BackupStage.DownloadingSheets => string.Format(CultureInfo.CurrentCulture, AppStrings.BackupProgressDownloadingFormat, Math.Min(p.Current + 1, p.Total), p.Total),
        BackupStage.SafetyCopy => AppStrings.BackupProgressSafetyCopy,
        BackupStage.Applying => AppStrings.BackupProgressApplying,
        _ => AppStrings.BackupProgressPreparing
    };

    public static string DescribeError(Exception ex) => ex switch
    {
        BackupNotFoundException => AppStrings.BackupNotFoundMessage,
        BackupFormatException => AppStrings.BackupBadFormatMessage,
        DriveAuthRequiredException => AppStrings.BackupAuthRequiredMessage,
        DriveQuotaException => AppStrings.BackupQuotaMessage,
        _ => string.Format(CultureInfo.CurrentCulture, AppStrings.BackupGenericFailureFormat, ex.Message)
    };

    private void SetBusy(string status)
    {
        StatusText = status;
        HasStatus = true;
        IsIdle = false;
    }

    private void SetIdle()
    {
        StatusText = string.Empty;
        HasStatus = false;
        IsIdle = true;
        Refresh();
    }
}
