using System.ComponentModel;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls.Shapes;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class PianoPage : ContentPage
{
    private const string PianoGlyph = "";
    private const string ChevronLeftGlyph = "";
    private const string ChevronRightGlyph = "";
    private const string MinusGlyph = "";
    private const string PlusGlyph = "";
    private const string SustainGlyph = "";

    private readonly PianoViewModel _viewModel;
    private Window? _window;
    private bool _isLandscape;
    private bool _orientationApplied;
    private int _readoutLength = -1;

    public PianoPage(PianoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Keyboard.KeyPressed += OnKeyPressed;
        Keyboard.KeyReleased += OnKeyReleased;
        MiniMap.FirstWhiteKeyRequested += (_, first) => _viewModel.SetFirstWhiteKey(first);
    }

    // The audio lease is only held while this page is visible. Attach never blocks: it asks the
    // service to load and start on the thread pool, so navigation is not delayed.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
        _window = Window;
        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
        }

        _viewModel.IsLandscape = Width > Height && Height > 0;
        ApplyOrientation();
        _viewModel.Attach();
        UpdateAll();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Keyboard.ReleaseAll();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window = null;
        }

        _viewModel.Detach();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _viewModel.IsLandscape = width > height;
        ApplyOrientation();
        // Portrait: about 44 % of the page, never under 260 dp, so the keys stay big and near the thumb.
        Keyboard.HeightRequest = _isLandscape
            ? Math.Max(120, height - 125)
            : Math.Max(260, height * 0.44);
        _readoutLength = -1;
        UpdateReadout();
    }

    private void OnWindowStopped(object? sender, EventArgs e) => Keyboard.ReleaseAll();

    private void OnKeyPressed(object? sender, PianoKeyEventArgs e)
    {
        _viewModel.NoteOn(e.Note, e.Velocity);
        MiniMap.SetPressed(e.Note, true);
    }

    private void OnKeyReleased(object? sender, PianoKeyEventArgs e)
    {
        _viewModel.NoteOff(e.Note);
        MiniMap.SetPressed(e.Note, false);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => UpdateAll();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PianoViewModel.FirstWhiteKey):
            case nameof(PianoViewModel.WhiteKeyCount):
                UpdateRange();
                break;
            case nameof(PianoViewModel.IsLandscape):
                ApplyOrientation();
                break;
            case nameof(PianoViewModel.Instrument):
                UpdateInstrument();
                break;
            case nameof(PianoViewModel.Labels):
            case nameof(PianoViewModel.ResolvedNaming):
                UpdateLabels();
                break;
            case nameof(PianoViewModel.SustainOn):
                UpdateSustain();
                break;
            case nameof(PianoViewModel.IsLoading):
            case nameof(PianoViewModel.LoadFailed):
                UpdateStatus();
                break;
            case nameof(PianoViewModel.ReadoutText):
            case nameof(PianoViewModel.ReadoutIsDim):
                UpdateReadout();
                break;
        }
    }

    private void UpdateAll()
    {
        UpdateIcons();
        UpdateRange();
        UpdateInstrument();
        UpdateLabels();
        UpdateSustain();
        UpdateStatus();
        UpdateReadout();
    }

    private void UpdateRange()
    {
        Keyboard.FirstWhiteKey = _viewModel.FirstWhiteKey;
        Keyboard.WhiteKeyCount = _viewModel.WhiteKeyCount;
        MiniMap.SetWindow(_viewModel.FirstWhiteKey, _viewModel.WhiteKeyCount);
    }

    private void UpdateInstrument()
    {
        var instrument = _viewModel.Instrument;
        InstrumentButton.Text = instrument.Name + " ▾";
        InstrumentButton.ImageSource = Icon(PianoGlyph, 18, "PrimaryLight", "PrimaryDark");
        InstrumentButton.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8);
        SemanticProperties.SetDescription(InstrumentButton, AppStrings.PianoInstrument + ": " + instrument.Name);
        Keyboard.SetInstrumentRange(instrument.LowNote, instrument.HighNote);
        MiniMap.SetInstrumentRange(instrument.LowNote, instrument.HighNote);
    }

    private void UpdateLabels()
    {
        var naming = _viewModel.ResolvedNaming;
        Keyboard.Naming = naming;
        Keyboard.Labels = _viewModel.Labels;

        // The chip is neutral in every mode: it shows the mode as text (a dash, C, or a short run of names).
        var (text, description) = _viewModel.Labels switch
        {
            PianoLabels.COnly => (NoteNames.Get(0, naming), AppStrings.PianoLabelsCOnly),
            PianoLabels.All => ($"{NoteNames.Get(0, naming)} {NoteNames.Get(2, naming)} {NoteNames.Get(4, naming)}", AppStrings.PianoLabelsAll),
            _ => ("–", AppStrings.PianoLabelsOff)
        };
        LabelsButton.Text = text;
        SemanticProperties.SetDescription(LabelsButton, description);
    }

    private void UpdateSustain()
    {
        // Off is the neutral chip, on the filled Primary tint, with the icon in the matching colour.
        var on = _viewModel.SustainOn;
        SustainButton.Style = (Style)Application.Current!.Resources[on ? "ChipSelected" : "Chip"];
        SustainButton.ImageSource = on
            ? Icon(SustainGlyph, 22, "OnPrimaryContainerLight", "OnPrimaryContainerDark")
            : Icon(SustainGlyph, 22, "TextSecondaryLight", "TextSecondaryDark");
        SemanticProperties.SetDescription(SustainButton, AppStrings.PianoSustain);
    }

    private void UpdateStatus()
    {
        Keyboard.StatusText = _viewModel.LoadFailed ? AppStrings.PianoLoadFailed : AppStrings.PianoLoading;
        Keyboard.IsLoading = _viewModel.IsLoading || _viewModel.LoadFailed;
    }

    private void UpdateReadout()
    {
        var text = _viewModel.ReadoutText;
        ReadoutLabel.Text = text;
        ReadoutLabel.TextColor = _viewModel.ReadoutIsDim
            ? Themed("TextMutedLight", "TextMutedDark")
            : Themed("TextPrimaryLight", "TextPrimaryDark");

        // Four long names ("Sol#4 · La#4 ...") must still fit on one line. The size only depends on
        // the text length, so it is recomputed when the length changes, not on every press.
        if (text.Length == _readoutLength)
        {
            return;
        }

        _readoutLength = text.Length;
        var baseSize = _isLandscape ? 28d : 68d;
        var available = Math.Max(Root.Width - (_isLandscape ? 300 : 24), 100);
        ReadoutLabel.FontSize = Math.Clamp(available / (Math.Max(text.Length, 1) * 0.6), 16, baseSize);
    }

    private void UpdateIcons()
    {
        OctaveDownButton.Source = Icon(ChevronLeftGlyph, 24, "TextPrimaryLight", "TextPrimaryDark");
        OctaveUpButton.Source = Icon(ChevronRightGlyph, 24, "TextPrimaryLight", "TextPrimaryDark");
        KeysMinusButton.Source = Icon(MinusGlyph, 24, "TextPrimaryLight", "TextPrimaryDark");
        KeysPlusButton.Source = Icon(PlusGlyph, 24, "TextPrimaryLight", "TextPrimaryDark");
    }

    // Portrait: title and chip, readout, mini-map, controls. Landscape: one row with chip,
    // readout and controls, then the mini-map and a keyboard that takes all the height.
    private void ApplyOrientation()
    {
        var landscape = _viewModel.IsLandscape;
        if (_orientationApplied && landscape == _isLandscape)
        {
            return;
        }

        _isLandscape = landscape;
        _orientationApplied = true;
        TitleLabel.IsVisible = !landscape;
        var rows = Root.RowDefinitions;
        var columns = ControlsRow.ColumnDefinitions;
        if (landscape)
        {
            rows[1].Height = new GridLength(0);
            Place(InstrumentButton, 0, 0, 1, new Thickness(0, 8, 0, 0));
            Place(ReadoutLabel, 0, 1, 1, new Thickness(8, 8, 8, 0));
            Place(ControlsRow, 0, 2, 1, new Thickness(0, 8, 0, 0));
            Place(MiniMap, 2, 0, 3, new Thickness(0, 4, 0, 4));
            Place(OctaveGroup, 0, 0, 1, default);
            Place(KeysGroup, 0, 2, 1, default);
            Place(TogglesRow, 0, 4, 1, default);
            columns[1].Width = new GridLength(12);
            columns[3].Width = new GridLength(12);
            TogglesRow.ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)];
            TogglesRow.ColumnSpacing = 4;
            SustainButton.Text = string.Empty;
            SustainButton.WidthRequest = 48;
            SustainButton.HeightRequest = 36;
            LabelsButton.HeightRequest = 36;
            LabelsButton.MinimumWidthRequest = 56;
            ReadoutLabel.HeightRequest = 44;
            ReadoutLabel.VerticalOptions = LayoutOptions.Fill;
        }
        else
        {
            rows[1].Height = GridLength.Star;
            Place(InstrumentButton, 0, 2, 1, new Thickness(0, 16, 0, 0));
            Place(ReadoutLabel, 1, 0, 3, new Thickness(0, 8));
            Place(ControlsRow, 3, 0, 3, new Thickness(0, 8, 0, 10));
            Place(MiniMap, 2, 0, 3, new Thickness(0, 4, 0, 4));
            Place(OctaveGroup, 0, 0, 1, default);
            Place(KeysGroup, 0, 4, 1, default);
            Place(TogglesRow, 1, 0, 5, new Thickness(0, 8, 0, 0));
            columns[1].Width = GridLength.Star;
            columns[3].Width = GridLength.Star;
            TogglesRow.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
            TogglesRow.ColumnSpacing = 12;
            SustainButton.Text = AppStrings.PianoSustain;
            SustainButton.WidthRequest = -1;
            SustainButton.HeightRequest = 44;
            LabelsButton.HeightRequest = 44;
            LabelsButton.MinimumWidthRequest = 0;
            ReadoutLabel.HeightRequest = -1;
            ReadoutLabel.VerticalOptions = LayoutOptions.Center;
        }

        _readoutLength = -1;
        UpdateReadout();
    }

    private static void Place(View view, int row, int column, int columnSpan, Thickness margin)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, columnSpan);
        view.Margin = margin;
    }

    private async void OnInstrumentClicked(object? sender, EventArgs e)
    {
        try
        {
            var sheet = new PianoInstrumentSheet(_viewModel.Instrument, Math.Max(Height * 0.7, 240), _viewModel.SelectInstrument);
            var options = new PopupOptions
            {
                CanBeDismissedByTappingOutsideOfPopup = true,
                PageOverlayColor = Color.FromArgb("#66000000"),
                Shape = new RoundRectangle { CornerRadius = new CornerRadius(20, 20, 0, 0), StrokeThickness = 0 },
                Shadow = null
            };
            await this.ShowPopupAsync(sheet, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing the instrument sheet failed: {ex.Message}");
        }
    }

    private static Color Themed(string lightKey, string darkKey) =>
        (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? darkKey : lightKey];

    private static FontImageSource Icon(string glyph, double size, string lightKey, string darkKey) => new()
    {
        Glyph = glyph,
        FontFamily = "MaterialOutlined",
        Size = size,
        Color = Themed(lightKey, darkKey)
    };
}
