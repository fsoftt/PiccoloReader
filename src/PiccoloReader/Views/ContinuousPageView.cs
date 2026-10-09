using System.ComponentModel;
using PiccoloReader.Core.ViewModels;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

// One page in the continuous reading mode's list: the rendered page image
// with its annotations drawn on top (read-only). Its height always follows
// the page's own aspect ratio, so the image and the annotation overlay
// cover exactly the same area. Annotations are stored relative to the
// single-page viewer's container (the page letterboxed in the reading
// area), so they're mapped onto the page through a PageFrame.
public class ContinuousPageView : ContentView
{
    // Gap below each page. Part of the item itself (not the list's
    // ItemSpacing, whose per-platform distribution isn't specified) so
    // ContinuousLayout can compute page positions exactly.
    public const double Spacing = 8;

    private readonly AnnotationPainter _painter;
    private readonly Grid _pageArea;
    private readonly Image _image;
    private readonly SKCanvasView _canvas;
    private readonly ActivityIndicator _loadingIndicator;
    private readonly Func<(double Width, double Height)> _editorSize;
    private ContinuousPage? _page;

    // editorSize is the single-page viewer's container size, which stored
    // annotation coordinates are relative to. onTapped receives the page and
    // the tap position in those same annotation coordinates. listZoom is the
    // list's pinch zoom: the gesture position is measured in screen pixels,
    // which the zoomed list magnifies, so it's divided back out.
    public ContinuousPageView(
        AnnotationPainter painter,
        Func<(double Width, double Height)> editorSize,
        Action<ContinuousPage, double, double> onTapped,
        Func<double> listZoom)
    {
        _painter = painter;
        _editorSize = editorSize;

        _image = new Image { Aspect = Aspect.AspectFit };
        _canvas = new SKCanvasView { InputTransparent = true };
        _canvas.PaintSurface += OnCanvasPaintSurface;
        _loadingIndicator = new ActivityIndicator
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _pageArea = new Grid
        {
            BackgroundColor = Colors.White,
            Margin = new Thickness(0, 0, 0, Spacing),
            Children = { _image, _canvas, _loadingIndicator }
        };
        Content = _pageArea;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            var position = e.GetPosition(_pageArea);
            if (_page is null || position is null || _pageArea.Width <= 0 || _pageArea.Height <= 0)
            {
                return;
            }

            var zoom = Math.Max(listZoom(), 1);
            var frame = CurrentFrame();
            onTapped(
                _page,
                frame.ToContainerX(position.Value.X / zoom / _pageArea.Width),
                frame.ToContainerY(position.Value.Y / zoom / _pageArea.Height));
        };
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

        var height = ContinuousLayout.PageHeight(_page, width) + Spacing;
        if (Math.Abs(HeightRequest - height) > 0.5)
        {
            HeightRequest = height;
        }
    }

    private PageFrame CurrentFrame()
    {
        var (width, height) = _editorSize();
        return _page is null ? PageFrame.Full : PageFrame.Fit(width, height, _page.AspectRatio);
    }

    private void OnCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        if (_page is not null)
        {
            _painter.DrawAnnotations(canvas, e.Info, _page.Annotations, CurrentFrame());
        }
    }
}
