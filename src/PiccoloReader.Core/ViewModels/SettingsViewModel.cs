using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.Services.Stylus;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILanguagePreferenceService _languagePreferenceService;
    private readonly ITunerStripPreferenceService _tunerStripPreferenceService;
    private readonly IStylusPreferenceService _stylusPreferenceService;
    private readonly IConcertModePreferenceService _concertModePreferenceService;
    private readonly IConcertTutorialPreferenceService _concertTutorialPreferenceService;
    private bool _refreshingTutorial;

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

    private string[]? _tunerStripPositionNames;
    private CultureInfo? _tunerStripPositionNamesCulture;

    public string[] TunerStripPositionNames
    {
        get
        {
            var culture = CultureInfo.CurrentUICulture;
            if (_tunerStripPositionNames is null || !Equals(_tunerStripPositionNamesCulture, culture))
            {
                _tunerStripPositionNames = new[]
                {
                    AppStrings.TunerStripPositionLeft,
                    AppStrings.TunerStripPositionRight,
                    AppStrings.TunerStripPositionBottom,
                    AppStrings.TunerStripPositionTop
                };
                _tunerStripPositionNamesCulture = culture;
            }

            return _tunerStripPositionNames;
        }
    }

    [ObservableProperty]
    private string _selectedLanguageCode;

    [ObservableProperty]
    private TunerStripPosition _tunerStripPosition;

    [ObservableProperty]
    private bool _stylusDrawsDirectly;

    [ObservableProperty]
    private bool _recognizeShapes;

    [ObservableProperty]
    private bool _concertModeEnabled;

    [ObservableProperty]
    private bool _concertTutorialEnabled;

    public SettingsViewModel(ILanguagePreferenceService languagePreferenceService, ITunerStripPreferenceService tunerStripPreferenceService, IStylusPreferenceService stylusPreferenceService, IConcertModePreferenceService concertModePreferenceService, IConcertTutorialPreferenceService concertTutorialPreferenceService)
    {
        _languagePreferenceService = languagePreferenceService;
        _tunerStripPreferenceService = tunerStripPreferenceService;
        _stylusPreferenceService = stylusPreferenceService;
        _stylusDrawsDirectly = stylusPreferenceService.DrawsDirectly;
        _recognizeShapes = stylusPreferenceService.RecognizesShapes;
        _concertModePreferenceService = concertModePreferenceService;
        _concertModeEnabled = concertModePreferenceService.Enabled;
        _concertTutorialPreferenceService = concertTutorialPreferenceService;
        _concertTutorialEnabled = ConcertTutorialPolicy.IsTutorialPending(concertTutorialPreferenceService);
        _selectedLanguageCode = languagePreferenceService.GetSavedLanguageCode() ?? "en";
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

    partial void OnStylusDrawsDirectlyChanged(bool value) =>
        _stylusPreferenceService.DrawsDirectly = value;

    partial void OnRecognizeShapesChanged(bool value) =>
        _stylusPreferenceService.RecognizesShapes = value;

    partial void OnConcertModeEnabledChanged(bool value) =>
        _concertModePreferenceService.Enabled = value;

    partial void OnConcertTutorialEnabledChanged(bool value)
    {
        if (_refreshingTutorial)
        {
            return;
        }

        if (value)
        {
            _concertTutorialPreferenceService.Reset();
        }
        else
        {
            _concertTutorialPreferenceService.LibrarySeen = true;
            _concertTutorialPreferenceService.ViewerSeen = true;
        }
    }

    /// <summary>Re-reads the tutorial flags (they change while the page is away) without writing them back.</summary>
    public void RefreshConcertTutorial()
    {
        _refreshingTutorial = true;
        try
        {
            ConcertTutorialEnabled = ConcertTutorialPolicy.IsTutorialPending(_concertTutorialPreferenceService);
        }
        finally
        {
            _refreshingTutorial = false;
        }
    }
}
