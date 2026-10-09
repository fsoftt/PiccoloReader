using System.ComponentModel;
using PiccoloReader.Core.ViewModels;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

// One page in the continuous reading mode's list: the rendered page image
// with its annotations drawn on top (read-only). Its height always follows
// the page's own aspect ratio, so the image and the annotation overlay
// cover exactly the same area and the normalized annotation coordinates
// line up the same way they do in the single-page viewer.
public class ContinuousPageView : ContentView
{
    private readonly AnnotationPainter _painter;
    private readonly Image _image;
    private readonly SKCanvasView _canvas;
    private readonly ActivityIndicator _loadingIndicator;
    private ContinuousPage? _page;

    public ContinuousPageView(AnnotationPainter painter, Action onTapped)
    {
        _painter = painter;

        _image = new Image { Aspect = Aspect.AspectFit };
        _canvas = new SKCanvasView { InputTransparent = true };
        _canvas.PaintSurface += OnCanvasPaintSurface;
        _loadingIndicator = new ActivityIndicator
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        BackgroundColor = Colors.White;
        Content = new Grid { Children = { _image, _canvas, _loadingIndicator } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTapped();
        GestureRecognizers.Add(tap);

        SizeChanged += (_, _) => UpdateHeight();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_page is not null)
        {
            _page.PropertyChanged -= OnPagePropertyChanged;
        }

        _page = BindingContext as ContinuousPage;

        if (_page is not null)
        {
            _page.PropertyChanged += OnPagePropertyChanged;
        }

        UpdateImage();
        UpdateHeight();
        _canvas.InvalidateSurface();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ContinuousPage.ImageBytes):
                UpdateImage();
                break;
            case nameof(ContinuousPage.AspectRatio):
                UpdateHeight();
                break;
            case nameof(ContinuousPage.Annotations):
                _canvas.InvalidateSurface();
                break;
        }
    }

    private void UpdateImage()
    {
        var bytes = _page?.ImageBytes;
        _image.Source = bytes is { Length: > 0 } ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;

        var loading = _page is not null && bytes is null;
        _loadingIndicator.IsVisible = loading;
        _loadingIndicator.IsRunning = loading;
    }

    // Before the first layout pass Width is still unknown, so the screen
    // width is used as an estimate - the list fills the screen width.
    private void UpdateHeight()
    {
        if (_page is null)
        {
            return;
        }

        var width = Width > 0
            ? Width
            : DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;

        var height = Math.Round(width * _page.AspectRatio);
        if (Math.Abs(HeightRequest - height) > 0.5)
        {
            HeightRequest = height;
        }
    }

    private void OnCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        if (_page is not null)
        {
            _painter.DrawAnnotations(canvas, e.Info, _page.Annotations);
        }
    }
}
