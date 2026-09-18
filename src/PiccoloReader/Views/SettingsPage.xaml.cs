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
        UpdateLanguageSelectionVisuals();
    }

    private async void OnEnglishTapped(object? sender, TappedEventArgs e)
    {
        await SelectLanguageAsync("en");
    }

    private async void OnSpanishTapped(object? sender, TappedEventArgs e)
    {
        await SelectLanguageAsync("es");
    }

    private async Task SelectLanguageAsync(string code)
    {
        if (_viewModel.SelectedLanguageCode == code)
        {
            return;
        }

        _viewModel.SetLanguageCommand.Execute(code);
        UpdateLanguageSelectionVisuals();

        await DisplayAlertAsync(AppStrings.RestartRequiredTitle, AppStrings.RestartRequiredMessage, AppStrings.OK);
    }

    // Mirrors SheetViewerPage's SetColorRingSelected pattern: the selected
    // row gets a colored stroke + a small drop shadow, the other reverts
    // to the plain ListItemCard style - same selection convention already
    // used elsewhere in this app, no new converter/binding machinery.
    private void UpdateLanguageSelectionVisuals()
    {
        SetRowSelected(EnglishRow, _viewModel.SelectedLanguageCode == "en");
        SetRowSelected(SpanishRow, _viewModel.SelectedLanguageCode == "es");
    }

    private void SetRowSelected(Border row, bool isSelected)
    {
        row.StrokeThickness = isSelected ? 2 : 1;
        row.Stroke = isSelected
            ? (Color)Application.Current!.Resources["Primary"]
            : (Color)Application.Current!.Resources["Gray200"];
        row.Shadow = isSelected
            ? new Shadow { Brush = Colors.Black, Opacity = 0.3f, Radius = 6, Offset = new Point(0, 2) }
            : null!;
    }
}
