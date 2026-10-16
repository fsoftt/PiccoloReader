using System.ComponentModel;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class MetronomePage : ContentPage
{
    private const double DotSize = 16;

    private readonly MetronomeViewModel _viewModel;
    private Border[] _dots = [];
    private int _litDot = -1;

    public MetronomePage(MetronomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // The page only listens to the view model (and the view model only to
    // the player's beats) while visible, so a hidden page costs nothing.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Attach();
        RebuildDots();
        UpdatePlayButton();
        UpdateSubdivisionValue();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Detach();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MetronomeViewModel.CurrentBeat):
                LightDot(_viewModel.CurrentBeat);
                break;
            case nameof(MetronomeViewModel.BeatsPerBar):
            case nameof(MetronomeViewModel.AccentFirstBeat):
                RebuildDots();
                break;
            case nameof(MetronomeViewModel.IsPlaying):
                UpdatePlayButton();
                break;
            case nameof(MetronomeViewModel.Subdivision):
                UpdateSubdivisionValue();
                break;
        }
    }

    private void UpdatePlayButton()
    {
        PlayGlyph.Glyph = _viewModel.IsPlaying ? "" : "";
        SemanticProperties.SetDescription(PlayButton, _viewModel.IsPlaying ? AppStrings.MetronomeStop : AppStrings.MetronomeStart);
    }

    private void UpdateSubdivisionValue() =>
        SubdivisionValueLabel.Text = GetSubdivisionName(_viewModel.Subdivision);

    private static string GetSubdivisionName(int subdivision) => subdivision switch
    {
        2 => AppStrings.SubdivisionEighths,
        3 => AppStrings.SubdivisionTriplets,
        4 => AppStrings.SubdivisionSixteenths,
        _ => AppStrings.SubdivisionNone
    };

    private static Color Themed(string lightKey, string darkKey) =>
        (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? darkKey : lightKey];

    private void RebuildDots()
    {
        var count = _viewModel.BeatsPerBar;
        if (_dots.Length != count)
        {
            DotsLayout.Children.Clear();
            _dots = new Border[count];
            for (var i = 0; i < count; i++)
            {
                _dots[i] = new Border
                {
                    WidthRequest = DotSize,
                    HeightRequest = DotSize,
                    Padding = 0,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = DotSize / 2 },
                    VerticalOptions = LayoutOptions.Center
                };
                DotsLayout.Children.Add(_dots[i]);
            }
        }

        _litDot = -1;
        for (var i = 0; i < _dots.Length; i++)
        {
            StyleDot(i, lit: false);
        }

        LightDot(_viewModel.CurrentBeat);
    }

    private void LightDot(int beat)
    {
        if (_litDot >= 0 && _litDot < _dots.Length)
        {
            StyleDot(_litDot, lit: false);
        }

        _litDot = beat >= 0 && beat < _dots.Length ? beat : -1;
        if (_litDot >= 0)
        {
            StyleDot(_litDot, lit: true);
        }
    }

    // Lit: primary (larger when the beat is the accent). Idle: the first
    // beat is tinted while the accent is on, the others use the outline tone.
    private void StyleDot(int index, bool lit)
    {
        var dot = _dots[index];
        var isAccent = index == 0 && _viewModel.AccentFirstBeat;
        if (lit)
        {
            dot.BackgroundColor = Themed("PrimaryLight", "PrimaryDark");
            dot.Scale = isAccent ? 1.3 : 1;
        }
        else
        {
            dot.BackgroundColor = isAccent
                ? Themed("OnPrimaryContainerLight", "OnPrimaryContainerDark")
                : Themed("OutlineLight", "OutlineDark");
            dot.Scale = 1;
        }
    }

    private async void OnBeatsPerBarRowTapped(object? sender, TappedEventArgs e)
    {
        var options = Enumerable.Range(MetronomeSettings.MinBeatsPerBar, MetronomeSettings.MaxBeatsPerBar)
            .Select(n => n.ToString())
            .ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.MetronomeBeatsPerBar, AppStrings.Cancel, null, options);
        if (int.TryParse(selected, out var beats) && options.Contains(selected))
        {
            _viewModel.BeatsPerBar = beats;
        }
    }

    private async void OnSubdivisionRowTapped(object? sender, TappedEventArgs e)
    {
        var options = Enumerable.Range(MetronomeSettings.MinSubdivision, MetronomeSettings.MaxSubdivision)
            .Select(GetSubdivisionName)
            .ToArray();
        var selected = await DisplayActionSheetAsync(AppStrings.MetronomeSubdivision, AppStrings.Cancel, null, options);
        var index = Array.IndexOf(options, selected);
        if (index >= 0)
        {
            _viewModel.Subdivision = MetronomeSettings.MinSubdivision + index;
        }
    }
}
