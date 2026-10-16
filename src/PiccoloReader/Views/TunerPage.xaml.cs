using System.ComponentModel;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class TunerPage : ContentPage
{
    private readonly TunerViewModel _viewModel;
    private readonly TunerNeedleDrawable _needle = new();
    private Border[] _presetChips = [];
    private Label[] _presetLabels = [];
    private static readonly string[] OctaveTexts = ["-1", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    private Window? _window;
    private string? _shownNote;
    private string? _shownOctaveText;
    private float _shownHz = -1f;
    private TunerState? _shownState;
    private float _density = 1f;

    public TunerPage(TunerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        NeedleView.Drawable = _needle;
        BuildPresets();
    }

    // The microphone is only open while this page is visible: attach on
    // appearing, detach on disappearing and when the app goes to the background.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
        _density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
        _window = Window;
        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
            _window.Resumed += OnWindowResumed;
        }

        UpdateAll();
        AttachInput();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window.Resumed -= OnWindowResumed;
            _window = null;
        }

        _viewModel.Detach();
    }

    private void OnWindowStopped(object? sender, EventArgs e) => _viewModel.Detach();

    private void OnWindowResumed(object? sender, EventArgs e) => AttachInput();

    private async void AttachInput()
    {
        try
        {
            await _viewModel.AttachAsync();
        }
        catch (Exception)
        {
            // A failed start leaves the tuner idle; nothing else to do.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TunerViewModel.State):
            case nameof(TunerViewModel.NoteName):
            case nameof(TunerViewModel.Octave):
            case nameof(TunerViewModel.FrequencyHz):
                UpdateReading();
                break;
            case nameof(TunerViewModel.Cents):
                UpdateNeedle();
                break;
            case nameof(TunerViewModel.NoteNaming):
                UpdateNotationValue();
                break;
            case nameof(TunerViewModel.A4Hz):
                UpdatePresets();
                break;
        }
    }

    private void UpdateAll()
    {
        _needle.TickColor = Themed("OutlineLight", "OutlineDark");
        NeedleView.Invalidate();
        UpdateReading();
        UpdateNotationValue();
        UpdatePresets();
    }

    private static Color Themed(string lightKey, string darkKey) =>
        (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? darkKey : lightKey];

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => UpdateAll();

    // Called on every VM change (about 20 Hz while a note is playing, never while
    // silent). Every setter is guarded by a cached value so unchanged data
    // costs a few comparisons and no layout pass, allocation or redraw.
    private void UpdateReading()
    {
        var state = _viewModel.State;
        var hasPitch = state == TunerState.Listening;

        SetText(NoteLabel, ref _shownNote, hasPitch ? _viewModel.NoteName : "-");
        SetText(OctaveLabel, ref _shownOctaveText, hasPitch ? OctaveText(_viewModel.Octave) : string.Empty);

        var hz = hasPitch ? _viewModel.FrequencyHz : 0f;
        if (hz != _shownHz)
        {
            _shownHz = hz;
            HzLabel.Text = hasPitch ? $"{hz:0.0} Hz" : string.Empty;
        }

        var opacity = hasPitch ? 1d : 0.4d;
        if (NoteLabel.Opacity != opacity)
        {
            NoteLabel.Opacity = opacity;
        }

        if (_shownState != state)
        {
            _shownState = state;
            StatusLabel.Text = state switch
            {
                TunerState.NoPermission => AppStrings.TunerNoPermission,
                TunerState.Unsupported => AppStrings.TunerUnsupported,
                TunerState.Error => AppStrings.TunerError,
                TunerState.Silent => AppStrings.TunerListening,
                _ => string.Empty
            };
            OpenSettingsButton.IsVisible = state == TunerState.NoPermission;
        }

        UpdateNeedle();
    }

    private static void SetText(Label label, ref string? shown, string text)
    {
        if (!string.Equals(shown, text, StringComparison.Ordinal))
        {
            shown = text;
            label.Text = text;
        }
    }

    private static string OctaveText(int octave) =>
        (uint)(octave + 1) < OctaveTexts.Length ? OctaveTexts[octave + 1] : octave.ToString();

    private void UpdateNeedle()
    {
        var hasPitch = _viewModel.State == TunerState.Listening;
        if (_needle.SetReading(hasPitch, _viewModel.Cents, (float)NeedleView.Width, _density))
        {
            NeedleView.Invalidate();
        }
    }

    private void UpdateNotationValue() => NotationValueLabel.Text = GetNotationName(_viewModel.NoteNaming);

    private static string GetNotationName(NoteNaming naming) => naming switch
    {
        NoteNaming.Letter => AppStrings.TunerNotationLetter,
        NoteNaming.Solfege => AppStrings.TunerNotationSolfege,
        _ => AppStrings.TunerNotationAuto
    };

    private void BuildPresets()
    {
        var presets = TunerSettings.Presets;
        _presetChips = new Border[presets.Count];
        _presetLabels = new Label[presets.Count];
        for (var i = 0; i < presets.Count; i++)
        {
            var hz = presets[i];
            var label = new Label
            {
                Text = hz.ToString(),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var chip = new Border
            {
                Style = (Style)Resources["PresetChip"],
                Padding = new Thickness(16, 0),
                Content = label,
                AutomationId = $"TunerPreset{hz}"
            };
            SemanticProperties.SetDescription(chip, $"A4 {hz} Hz");
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = _viewModel.SetA4PresetCommand, CommandParameter = hz });
            _presetChips[i] = chip;
            _presetLabels[i] = label;
            PresetsLayout.Children.Add(chip);
        }
    }

    private void UpdatePresets()
    {
        var presets = TunerSettings.Presets;
        for (var i = 0; i < presets.Count; i++)
        {
            var selected = presets[i] == _viewModel.A4Hz;
            _presetChips[i].BackgroundColor = selected
                ? Themed("PrimaryContainerLight", "PrimaryContainerDark")
                : Colors.Transparent;
            _presetLabels[i].TextColor = selected
                ? Themed("OnPrimaryContainerLight", "OnPrimaryContainerDark")
                : Themed("TextPrimaryLight", "TextPrimaryDark");
        }
    }

    private async void OnNotationRowTapped(object? sender, TappedEventArgs e)
    {
        NoteNaming[] values = [NoteNaming.Auto, NoteNaming.Letter, NoteNaming.Solfege];
        var options = values.Select(GetNotationName).ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.TunerNotation, AppStrings.Cancel, null, options);
        var index = Array.IndexOf(options, selected);
        if (index >= 0)
        {
            _viewModel.NoteNaming = values[index];
        }
    }
}
