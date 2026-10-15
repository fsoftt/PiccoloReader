using System.ComponentModel;
using PiccoloReader.Core.ViewModels;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

// One page in the continuous reading mode's list: the rendered page image
// with its annotations drawn on top (read-only). Its height always follows
// the page's own aspect ratio, so the image and the annotation overlay
// cover exactly the same area, which is also what annotations are normalized
// to, so they are drawn and hit-tested without any mapping. A cropped page is
// shown as its cropped part only: the row follows the crop's aspect ratio and
// annotations (still full-page normalized) are mapped through the crop.
public class ContinuousPageView : ContentView
{
    // Gap below each page. Part of the item itself (not the list's
    // ItemSpacing, whose per-platform distribution isn't specified) so
    // ContinuousLayout can compute page positions exactly.
    public const double Spacing = 8;

    private readonly AnnotationPainter _painter;
    private readonly Grid _pageArea;
    private readonly Image _image;
    private readonly Image _detail;
    private readonly SKCanvasView _canvas;
    private readonly SKCanvasView _annotationDetail;
    private RectD _detailArea;
    private bool _annotationDetailPainted;
    private readonly ActivityIndicator _loadingIndicator;
    private ContinuousPage? _page;

    // onTapped receives the page and the tap position in annotation (page
    // normalized) coordinates. listZoom is the list's pinch zoom: the gesture
    // position is measured in screen pixels, which the zoomed list magnifies,
    // so it's divided back out.
    public ContinuousPageView(
        AnnotationPainter painter,
        Action<ContinuousPage, double, double> onTapped,
        Func<double> listZoom)
    {
        _painter = painter;

        _image = new Image { Aspect = Aspect.AspectFit };
        _detail = new Image
        {
            Aspect = Aspect.Fill,
            IsVisible = false,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        _canvas = new SKCanvasView { InputTransparent = true };
        _canvas.PaintSurface += OnCanvasPaintSurface;
        _annotationDetail = new SKCanvasView
        {
            IsVisible = false,
            InputTransparent = true,
            AnchorX = 0,
            AnchorY = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        _annotationDetail.PaintSurface += OnAnnotationDetailPaintSurface;
        _loadingIndicator = new ActivityIndicator
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _pageArea = new Grid
        {
            BackgroundColor = Colors.White,
            Margin = new Thickness(0, 0, 0, Spacing),
            Children = { _image, _detail, _canvas, _annotationDetail, _loadingIndicator }
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
            var frame = PageFrame.Fit(_pageArea.Width, _pageArea.Height, _page.FullAspectRatio, _page.Crop);
            onTapped(
                _page,
                frame.ToPageX(position.Value.X / zoom / _pageArea.Width),
                frame.ToPageY(position.Value.Y / zoom / _pageArea.Height));
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

        HideDetail();
        UpdateImage();
        UpdateHeight();
        _canvas.InvalidateSurface();
    }

    public ContinuousPage? Page => _page;

    // Sharp re-render of the part of this page that is on screen while the
    // list is zoomed in. `area` is in this page's own unscaled space, so the
    // list's zoom magnifies it together with the page image below it.
    public void ShowDetail(byte[] bytes, RectD area, double pixelsPerUnit, double zoom)
    {
        _detail.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        _detail.WidthRequest = DetailRegion.LayoutRequest(area.Width, pixelsPerUnit);
        _detail.HeightRequest = DetailRegion.LayoutRequest(area.Height, pixelsPerUnit);
        _detail.TranslationX = area.X;
        _detail.TranslationY = area.Y;
        _detail.IsVisible = true;

        // Annotations over the same area at screen resolution: laid out
        // `zoom` times larger and scaled back down by the list's zoom.
        _detailArea = area;
        _annotationDetail.WidthRequest = DetailRegion.LayoutRequest(area.Width * zoom, pixelsPerUnit);
        _annotationDetail.HeightRequest = DetailRegion.LayoutRequest(area.Height * zoom, pixelsPerUnit);
        _annotationDetail.Scale = 1 / zoom;
        _annotationDetail.TranslationX = area.X;
        _annotationDetail.TranslationY = area.Y;
        _annotationDetailPainted = false;
        _annotationDetail.IsVisible = true;
        _annotationDetail.InvalidateSurface();
    }

    public void HideDetail()
    {
        if (_detail.IsVisible || _detail.Source is not null)
        {
            _detail.IsVisible = false;
            _detail.Source = null;
        }

        if (_annotationDetail.IsVisible)
        {
            // The base canvas draws the annotations again.
            _annotationDetail.IsVisible = false;
            _annotationDetailPainted = false;
            _canvas.InvalidateSurface();
        }
    }

    private void InvalidateCanvases()
    {
        _canvas.InvalidateSurface();
        if (_annotationDetail.IsVisible)
        {
            _annotationDetail.InvalidateSurface();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ContinuousPage.ImageBytes):
                HideDetail();
                UpdateImage();
                break;
            case nameof(ContinuousPage.AspectRatio):
                HideDetail();
                UpdateHeight();
                break;
            case nameof(ContinuousPage.Crop):
                HideDetail();
                _canvas.InvalidateSurface();
                break;
            case nameof(ContinuousPage.Annotations):
                InvalidateCanvases();
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
    private void UpdateHeight(double? listWidth = null)
    {
        if (_page is null)
        {
            return;
        }

        var width = listWidth ?? (Width > 0
            ? Width
            : DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density);

        var height = ContinuousLayout.PageHeight(_page, width) + Spacing;
        if (Math.Abs(HeightRequest - height) > 0.5)
        {
            HeightRequest = height;
        }
    }

    // The page repaints when asked to, e.g. after the editor changed its
    // annotations while the list was hidden.
    public void InvalidateAnnotations() => InvalidateCanvases();

    // Item heights are derived from the list width, which can change while
    // the list is hidden (rotation with the editor open); Width is stale then.
    public void Relayout(double listWidth)
    {
        HideDetail();
        UpdateHeight(listWidth);
        _canvas.InvalidateSurface();
    }

    private void OnCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        if (_page is not null && !(_annotationDetail.IsVisible && _annotationDetailPainted))
        {
            // The image is AspectFit inside _pageArea, whose height can lag the
            // page aspect for a moment (row relayout after a rotation), so the
            // page rect is derived from the canvas as it is now rather than
            // assuming the canvas is exactly the page.
            var frame = PageFrame.Fit(e.Info.Width, e.Info.Height, _page.FullAspectRatio, _page.Crop);
            _painter.DrawAnnotations(canvas, e.Info, _page.Annotations, frame, _page.Crop);
        }
    }

    // The part of the page in _detailArea, drawn like the base canvas but at
    // k pixels per unit and shifted so the area's corner is the origin.
    private void OnAnnotationDetailPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        if (_page is null || _detailArea.IsEmpty || _canvas.Width <= 0 || _canvas.Height <= 0)
        {
            return;
        }

        var k = e.Info.Width / _detailArea.Width;
        var width = (float)(_canvas.Width * k);
        var height = (float)(_canvas.Height * k);
        canvas.Translate((float)(-_detailArea.X * k), (float)(-_detailArea.Y * k));
        var frame = PageFrame.Fit(width, height, _page.FullAspectRatio, _page.Crop);
        _painter.DrawAnnotations(canvas, width, height, _page.Annotations, frame, _page.Crop);

        // The base canvas keeps drawing until this has really painted, so the
        // annotations never go missing for a frame while the overlay appears.
        if (!_annotationDetailPainted)
        {
            _annotationDetailPainted = true;
            _canvas.InvalidateSurface();
        }
    }
}
