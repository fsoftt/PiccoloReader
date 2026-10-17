using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILanguagePreferenceService _languagePreferenceService;
    private readonly IAdsPreferenceService _adsPreferenceService;
    private readonly ITunerStripPreferenceService _tunerStripPreferenceService;

    // Parallel arrays backing the language Picker: index i in one
    // corresponds to index i in the other. Language names are shown in
    // their own native form regardless of current UI language (standard
    // convention - see AppStrings, which deliberately has no entries for
    // these), so they're plain literals, not AppStrings lookups.
    public string[] LanguageDisplayNames { get; } = { "English", "Español" };

    public string[] LanguageCodes { get; } = { "en", "es" };

    // Parallel arrays backing the tuner strip position action sheet. Names are
    // looked up on access so they follow the current UI language.
    public TunerStripPosition[] TunerStripPositions { get; } =
        { TunerStripPosition.Left, TunerStripPosition.Right, TunerStripPosition.Bottom, TunerStripPosition.Top };

    public string[] TunerStripPositionNames => new[]
    {
        AppStrings.TunerStripPositionLeft,
        AppStrings.TunerStripPositionRight,
        AppStrings.TunerStripPositionBottom,
        AppStrings.TunerStripPositionTop
    };

    [ObservableProperty]
    private string _selectedLanguageCode;

    [ObservableProperty]
    private TunerStripPosition _tunerStripPosition;

    [ObservableProperty]
    private bool _supportWithAdsEnabled;

    public SettingsViewModel(ILanguagePreferenceService languagePreferenceService, IAdsPreferenceService adsPreferenceService, ITunerStripPreferenceService tunerStripPreferenceService)
    {
        _languagePreferenceService = languagePreferenceService;
        _adsPreferenceService = adsPreferenceService;
        _tunerStripPreferenceService = tunerStripPreferenceService;
        _selectedLanguageCode = languagePreferenceService.GetSavedLanguageCode() ?? "en";
        _supportWithAdsEnabled = adsPreferenceService.GetSupportWithAdsEnabled();
        _tunerStripPosition = tunerStripPreferenceService.Position;
    }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        _languagePreferenceService.SaveLanguageCode(code);
        SelectedLanguageCode = code;
    }

    partial void OnTunerStripPositionChanged(TunerStripPosition value) =>
        _tunerStripPreferenceService.Position = value;

    partial void OnSupportWithAdsEnabledChanged(bool value) =>
        _adsPreferenceService.SetSupportWithAdsEnabled(value);
}
