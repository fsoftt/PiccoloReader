using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILanguagePreferenceService _languagePreferenceService;

    [ObservableProperty]
    private string _selectedLanguageCode;

    public SettingsViewModel(ILanguagePreferenceService languagePreferenceService)
    {
        _languagePreferenceService = languagePreferenceService;
        _selectedLanguageCode = languagePreferenceService.GetSavedLanguageCode() ?? "en";
    }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        _languagePreferenceService.SaveLanguageCode(code);
        SelectedLanguageCode = code;
    }
}
