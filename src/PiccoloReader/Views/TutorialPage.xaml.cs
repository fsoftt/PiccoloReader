using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class TutorialPage : ContentPage
{
    public TutorialPage(TutorialViewModel viewModel)
    {
        InitializeComponent();

        // Wired here rather than via x:Reference in XAML - the indicator is
        // declared after the carousel, and a forward x:Reference can fail to
        // resolve at XAML load.
        SlidesCarousel.IndicatorView = SlideIndicator;
        BindingContext = viewModel;
        ViewModel = viewModel;
        SizeChanged += (_, _) => viewModel.ShowSlideIcon = Height <= 0 || Width <= Height;
    }

    public TutorialViewModel ViewModel { get; }
}
