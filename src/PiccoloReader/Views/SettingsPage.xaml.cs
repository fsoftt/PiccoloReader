using System.Globalization;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;
using PiccoloReader.Services;

namespace PiccoloReader.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly LibraryRecoveryFlow _recoveryFlow;

    public SettingsPage(SettingsViewModel viewModel, LibraryRecoveryFlow recoveryFlow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _recoveryFlow = recoveryFlow;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RefreshLanguageValue();
        await RefreshStorageStatusAsync();
        VersionLabel.Text = string.Format(CultureInfo.CurrentUICulture, AppStrings.AppVersionFormat, AppInfo.VersionString);
    }

    private async Task RefreshStorageStatusAsync()
    {
        var lastSync = _recoveryFlow.State.LastSyncUtc;
        var text = lastSync is { } utc
            ? string.Format(CultureInfo.CurrentUICulture, AppStrings.StorageLastSyncFormat, utc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture))
            : AppStrings.StorageNeverSynced;

        var pending = await _recoveryFlow.Sync.CountPendingAsync();
        if (pending > 0)
        {
            text += " · " + string.Format(CultureInfo.CurrentUICulture, AppStrings.StoragePendingFormat, pending);
        }

        StorageSyncLabel.Text = text;
    }

    private async void OnRecoverLibraryTapped(object? sender, TappedEventArgs e)
    {
        await _recoveryFlow.RunRecoveryAsync(this);
        await RefreshStorageStatusAsync();
    }

    private void RefreshLanguageValue()
    {
        var index = Array.IndexOf(_viewModel.LanguageCodes, _viewModel.SelectedLanguageCode);
        LanguageValueLabel.Text = index >= 0 ? _viewModel.LanguageDisplayNames[index] : string.Empty;
    }

    private async void OnLanguageRowTapped(object? sender, TappedEventArgs e)
    {
        var selected = await DisplayActionSheetAsync(AppStrings.LanguageSectionHeader, AppStrings.Cancel, null, _viewModel.LanguageDisplayNames);
        var index = Array.IndexOf(_viewModel.LanguageDisplayNames, selected);
        if (index < 0)
        {
            return;
        }

        var code = _viewModel.LanguageCodes[index];
        if (_viewModel.SelectedLanguageCode == code)
        {
            return;
        }

        _viewModel.SetLanguageCommand.Execute(code);
        RefreshLanguageValue();

        await DisplayAlertAsync(AppStrings.RestartRequiredTitle, AppStrings.RestartRequiredMessage, AppStrings.OK);
    }

    private async void OnCreditLinkTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://fsoftt.github.io"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open credit URL: {ex.Message}");
        }
    }

    private async void OnShowTutorialClicked(object? sender, TappedEventArgs e)
    {
        var tutorialPage = Handler?.MauiContext?.Services.GetService<TutorialPage>();
        if (tutorialPage is null)
        {
            return;
        }

        tutorialPage.ViewModel.Finished += async (_, _) => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(tutorialPage);
    }

    private async void OnPrivacyPolicyClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(LegalLinks.GetPrivacyPolicyUrl(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not open privacy policy. Please try again.", "OK");
            System.Diagnostics.Debug.WriteLine($"Failed to open privacy policy URL: {ex.Message}");
        }
    }

    private async void OnTermsOfServiceClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(LegalLinks.GetTermsOfServiceUrl(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not open terms of service. Please try again.", "OK");
            System.Diagnostics.Debug.WriteLine($"Failed to open terms of service URL: {ex.Message}");
        }
    }
}
