using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILanguagePreferenceService _languagePreferenceService;
    private readonly IAdsPreferenceService _adsPreferenceService;

    // Parallel arrays backing the language Picker: index i in one
    // corresponds to index i in the other. Language names are shown in
    // their own native form regardless of current UI language (standard
    // convention - see AppStrings, which deliberately has no entries for
    // these), so they're plain literals, not AppStrings lookups.
    public string[] LanguageDisplayNames { get; } = { "English", "Español" };

    public string[] LanguageCodes { get; } = { "en", "es" };

    [ObservableProperty]
    private string _selectedLanguageCode;

    [ObservableProperty]
    private bool _supportWithAdsEnabled;

    public SettingsViewModel(ILanguagePreferenceService languagePreferenceService, IAdsPreferenceService adsPreferenceService)
    {
        _languagePreferenceService = languagePreferenceService;
        _adsPreferenceService = adsPreferenceService;
        _selectedLanguageCode = languagePreferenceService.GetSavedLanguageCode() ?? "en";
        _supportWithAdsEnabled = adsPreferenceService.GetSupportWithAdsEnabled();
    }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        _languagePreferenceService.SaveLanguageCode(code);
        SelectedLanguageCode = code;
    }

    partial void OnSupportWithAdsEnabledChanged(bool value) =>
        _adsPreferenceService.SetSupportWithAdsEnabled(value);
}
