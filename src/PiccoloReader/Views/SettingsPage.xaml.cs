using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LanguagePicker.SelectedIndex = Array.IndexOf(_viewModel.LanguageCodes, _viewModel.SelectedLanguageCode);
    }

    // Fires both for user selection AND the OnAppearing sync above (MAUI's
    // Picker raises this whenever SelectedIndex changes, programmatically
    // or not) - the SelectedLanguageCode equality check below is what
    // makes the sync-on-appear a no-op instead of popping the restart
    // dialog every time the page loads.
    private async void OnLanguagePickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        var index = LanguagePicker.SelectedIndex;
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

        await DisplayAlertAsync(AppStrings.RestartRequiredTitle, AppStrings.RestartRequiredMessage, AppStrings.OK);
    }

    private async void OnPrivacyPolicyClicked(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://fsoftt.github.io/PiccoloReader/privacy-policy"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not open privacy policy. Please try again.", "OK");
            System.Diagnostics.Debug.WriteLine($"Failed to open privacy policy URL: {ex.Message}");
        }
    }

    private async void OnTermsOfServiceClicked(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://fsoftt.github.io/PiccoloReader/terms-of-service"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not open terms of service. Please try again.", "OK");
            System.Diagnostics.Debug.WriteLine($"Failed to open terms of service URL: {ex.Message}");
        }
    }
}
