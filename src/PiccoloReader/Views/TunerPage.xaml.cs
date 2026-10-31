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

    // Hz readout is written at most every HzIntervalMs while a note is held;
    // a single timer (created once) delivers the trailing update.
    private const int HzIntervalMs = 250;
    private readonly IDispatcherTimer _hzTimer;
    private float _pendingHz;
    private bool _hzPending;
    private long _lastHzWriteMs;

    // Needle updates are coalesced: property changes mark it dirty and one
    // posted callback applies the latest reading.
    private readonly Action _applyNeedle;
    private bool _needleQueued;

    // Needle redraws are capped at one per NeedleIntervalMs while a note is
    // held; one timer (created once) delivers the trailing update.
    private const int NeedleIntervalMs = 100;
    private readonly IDispatcherTimer _needleTimer;
    private long _lastNeedleMs;

    public TunerPage(TunerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        NeedleView.Drawable = _needle;
        _applyNeedle = ApplyNeedle;
        _hzTimer = Dispatcher.CreateTimer();
        _hzTimer.Interval = TimeSpan.FromMilliseconds(HzIntervalMs);
        _hzTimer.IsRepeating = false;
        _hzTimer.Tick += OnHzTimerTick;
        _needleTimer = Dispatcher.CreateTimer();
        _needleTimer.IsRepeating = false;
        _needleTimer.Tick += OnNeedleTimerTick;
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
        _hzTimer.Stop();
        _needleTimer.Stop();
        _hzPending = false;
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
                QueueNeedle();
                break;
            case nameof(TunerViewModel.NoteNaming):
                UpdateNotationValue();
                break;
            case nameof(TunerViewModel.A4Hz):
                UpdatePresets();
                break;
            case nameof(TunerViewModel.TransposeKey):
            case nameof(TunerViewModel.OctaveShift):
                UpdateTransposition();
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
        UpdateTransposition();
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

        // While silent the last note stays, dimmed; "-" only before any note.
        var hasNote = _viewModel.NoteName.Length > 0;
        var noteChanged = SetText(NoteLabel, ref _shownNote, hasNote ? _viewModel.NoteName : "-");
        noteChanged |= SetText(OctaveLabel, ref _shownOctaveText, hasNote ? OctaveText(_viewModel.Octave) : string.Empty);

        UpdateHz(hasPitch, hasPitch ? _viewModel.FrequencyHz : 0f, noteChanged);

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

        QueueNeedle();
    }

    private void UpdateHz(bool hasPitch, float hz, bool noteChanged)
    {
        if (!hasPitch)
        {
            _hzTimer.Stop();
            _hzPending = false;
            if (_shownHz != 0f)
            {
                _shownHz = 0f;
                HzLabel.Text = string.Empty;
            }

            return;
        }

        if (hz == _shownHz)
        {
            _hzPending = false;
            return;
        }

        var now = Environment.TickCount64;
        if (noteChanged || _shownHz <= 0f || now - _lastHzWriteMs >= HzIntervalMs)
        {
            _hzTimer.Stop();
            _hzPending = false;
            WriteHz(hz, now);
            return;
        }

        _pendingHz = hz;
        _hzPending = true;
        if (!_hzTimer.IsRunning)
        {
            _hzTimer.Start();
        }
    }

    private void WriteHz(float hz, long now)
    {
        _shownHz = hz;
        _lastHzWriteMs = now;
        HzLabel.Text = $"{hz:0.0} Hz";
    }

    private void OnHzTimerTick(object? sender, EventArgs e)
    {
        _hzTimer.Stop();
        if (_hzPending && _viewModel.State == TunerState.Listening)
        {
            _hzPending = false;
            WriteHz(_pendingHz, Environment.TickCount64);
        }
    }

    private static bool SetText(Label label, ref string? shown, string text)
    {
        if (string.Equals(shown, text, StringComparison.Ordinal))
        {
            return false;
        }

        shown = text;
        label.Text = text;
        return true;
    }

    private static string OctaveText(int octave) =>
        (uint)(octave + 1) < OctaveTexts.Length ? OctaveTexts[octave + 1] : octave.ToString();

    private void QueueNeedle()
    {
        if (_needleQueued)
        {
            return;
        }

        // Clearing (silence, errors) and the first reading of a note apply at once;
        // while a note is held, redraws are spaced NeedleIntervalMs apart.
        if (_viewModel.State == TunerState.Listening)
        {
            var wait = NeedleIntervalMs - (Environment.TickCount64 - _lastNeedleMs);
            if (wait > 0)
            {
                if (!_needleTimer.IsRunning)
                {
                    _needleTimer.Interval = TimeSpan.FromMilliseconds(wait);
                    _needleTimer.Start();
                }

                return;
            }
        }

        _needleTimer.Stop();
        _needleQueued = true;
        Dispatcher.Dispatch(_applyNeedle);
    }

    private void OnNeedleTimerTick(object? sender, EventArgs e)
    {
        _needleTimer.Stop();
        ApplyNeedle();
    }

    private void ApplyNeedle()
    {
        _needleQueued = false;
        _lastNeedleMs = Environment.TickCount64;
        var hasPitch = _viewModel.State == TunerState.Listening;
        if (_needle.SetReading(hasPitch, _viewModel.Cents, (float)NeedleView.Width, _density))
        {
            NeedleView.Invalidate();
        }
    }

    private void UpdateNotationValue() => NotationValueLabel.Text = GetNotationName(_viewModel.NoteNaming);

    private void UpdateTransposition()
    {
        TransposeKeyValueLabel.Text = GetKeyName(_viewModel.TransposeKey);
        TransposeOctaveValueLabel.Text = GetOctaveName(_viewModel.OctaveShift);
    }

    private static string GetKeyName(TransposeKey key) => key switch
    {
        TransposeKey.Bb => "B♭",
        TransposeKey.Eb => "E♭",
        _ => key.ToString()
    };

    private static string GetOctaveName(int shift) => shift > 0 ? $"+{shift}" : shift.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private async void OnTransposeKeyRowTapped(object? sender, TappedEventArgs e)
    {
        TransposeKey[] values = [TransposeKey.C, TransposeKey.Bb, TransposeKey.A, TransposeKey.Eb, TransposeKey.F, TransposeKey.G];
        var options = values.Select(GetKeyName).ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.TunerTransposeKey, AppStrings.Cancel, null, options);
        var index = Array.IndexOf(options, selected);
        if (index >= 0)
        {
            _viewModel.TransposeKey = values[index];
        }
    }

    private async void OnTransposeOctaveRowTapped(object? sender, TappedEventArgs e)
    {
        var values = Enumerable.Range(TunerSettings.MinOctaveShift, TunerSettings.MaxOctaveShift - TunerSettings.MinOctaveShift + 1).ToArray();
        var options = values.Select(GetOctaveName).ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.TunerTransposeOctave, AppStrings.Cancel, null, options);
        var index = Array.IndexOf(options, selected);
        if (index >= 0)
        {
            _viewModel.OctaveShift = values[index];
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
