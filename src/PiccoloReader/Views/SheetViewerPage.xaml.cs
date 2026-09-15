using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(SheetId), "sheetId")]
public partial class SheetViewerPage : ContentPage
{
    private readonly SheetViewerViewModel _viewModel;

    private double _currentScale = 1;
    private double _startScale = 1;
    private double _xOffset;
    private double _yOffset;

    public SheetViewerPage(SheetViewerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public string SheetId { get; set; } = string.Empty;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
        var targetWidthPx = (int)displayInfo.Width;
        var targetHeightPx = (int)displayInfo.Height;

        await _viewModel.LoadAsync(sheetId, targetWidthPx, targetHeightPx);
        ResetZoom();
    }

    private void ResetZoom()
    {
        _currentScale = 1;
        _startScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        PageImage.Scale = 1;
        PageImage.TranslationX = 0;
        PageImage.TranslationY = 0;
    }

    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            // Ignore tap-to-turn while zoomed in - the user is most likely
            // trying to look around the page, not turn it.
            return;
        }

        var position = e.GetPosition(PageImage);
        if (position is null)
        {
            return;
        }

        if (position.Value.X < PageImage.Width / 2)
        {
            if (_viewModel.PreviousPageCommand.CanExecute(null))
            {
                _viewModel.PreviousPageCommand.Execute(null);
                ResetZoom();
            }
        }
        else
        {
            if (_viewModel.NextPageCommand.CanExecute(null))
            {
                _viewModel.NextPageCommand.Execute(null);
                ResetZoom();
            }
        }
    }

    private void OnSwipedLeft(object? sender, SwipedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            return;
        }

        if (_viewModel.NextPageCommand.CanExecute(null))
        {
            _viewModel.NextPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void OnSwipedRight(object? sender, SwipedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            return;
        }

        if (_viewModel.PreviousPageCommand.CanExecute(null))
        {
            _viewModel.PreviousPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = PageImage.Scale;
            PageImage.AnchorX = 0;
            PageImage.AnchorY = 0;
        }

        if (e.Status == GestureStatus.Running)
        {
            _currentScale = Math.Max(1, _startScale * e.Scale);

            var renderedX = PageImage.X + _xOffset;
            var deltaX = renderedX / PageImage.Width;
            var deltaWidth = PageImage.Width / (PageImage.Width * _startScale);
            var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

            var renderedY = PageImage.Y + _yOffset;
            var deltaY = renderedY / PageImage.Height;
            var deltaHeight = PageImage.Height / (PageImage.Height * _startScale);
            var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

            var targetX = _xOffset - (originX * PageImage.Width * (_currentScale - _startScale));
            var targetY = _yOffset - (originY * PageImage.Height * (_currentScale - _startScale));

            PageImage.TranslationX = targetX;
            PageImage.TranslationY = targetY;
            PageImage.Scale = _currentScale;
        }

        if (e.Status == GestureStatus.Completed)
        {
            _xOffset = PageImage.TranslationX;
            _yOffset = PageImage.TranslationY;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentScale <= 1.05)
        {
            // Not zoomed in - don't pan (also avoids fighting the swipe-to-turn gestures).
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                PageImage.TranslationX = _xOffset + e.TotalX;
                PageImage.TranslationY = _yOffset + e.TotalY;
                break;

            case GestureStatus.Completed:
                _xOffset = PageImage.TranslationX;
                _yOffset = PageImage.TranslationY;
                break;
        }
    }
}
