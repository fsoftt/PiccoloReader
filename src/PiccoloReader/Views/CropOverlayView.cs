using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

// Crop mode: the page is shown whole, with the area outside the crop dimmed
// and a crop rectangle that is dragged by 4 edge and 4 corner handles. The
// result is a PageCrop (normalized to the full page); the viewer persists it.
public class CropOverlayView : Grid
{
    // Touch targets are at least 44dp, however small the visible handle is.
    private const double TouchSize = 44;
    private const double MaxEdgeTouchLength = 96;

    private readonly Grid _area;
    private readonly Image _image;
    private readonly SKCanvasView _canvas;
    private readonly Dictionary<CropHandle, Border> _handles = new();
    private readonly List<object> _listeners = new();

    private double _aspectRatio;
    private PageCrop _crop = PageCrop.Full;
    private PageCrop _dragStart = PageCrop.Full;

    public CropOverlayView()
    {
        AutomationId = "CropOverlay";
        IsVisible = false;
        RowDefinitions = new RowDefinitionCollection
        {
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Star),
            new RowDefinition(GridLength.Auto)
        };
        this.SetAppThemeColor(BackgroundColorProperty, Colors.White, Colors.Black);

        // Swallows touches so the page underneath is not panned or tapped.
        GestureRecognizers.Add(new TapGestureRecognizer());

        var hint = new Label
        {
            Text = AppStrings.CropHint,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(16, 16, 16, 4),
            InputTransparent = true
        };
        hint.SetAppThemeColor(Label.TextColorProperty, ThemeColor("TextSecondaryLight"), ThemeColor("TextSecondaryDark"));
        AddAt(this, hint, 0, 0);

        _image = new Image { Aspect = Aspect.AspectFit, InputTransparent = true };
        _canvas = new SKCanvasView { InputTransparent = true };
        _canvas.PaintSurface += OnPaintSurface;

        _area = new Grid { Margin = new Thickness(16, 8) };
        _area.Children.Add(_image);
        _area.Children.Add(_canvas);
        _area.SizeChanged += (_, _) => Refresh();

        // Edges first so the corners, which can overlap them on small crops,
        // are on top and win the touch.
        foreach (var handle in new[]
        {
            CropHandle.Top, CropHandle.Bottom, CropHandle.Left, CropHandle.Right,
            CropHandle.TopLeft, CropHandle.TopRight, CropHandle.BottomLeft, CropHandle.BottomRight
        })
        {
            var border = CreateHandle(handle);
            _handles[handle] = border;
            _area.Children.Add(border);
        }

        AddAt(this, _area, 0, 1);
        AddAt(this, CreateBottomBar(), 0, 2);
    }

    public event EventHandler<PageCrop>? Applied;

    public event EventHandler? Cancelled;

    // The crop being edited (exposed for tests and automation).
    public PageCrop Crop => _crop;

    public void Show(byte[] pageImage, double fullAspectRatio, PageCrop crop)
    {
        _aspectRatio = fullAspectRatio;
        _crop = crop.OrFull();
        _image.Source = ImageSource.FromStream(() => new MemoryStream(pageImage));
        IsVisible = true;
        Refresh();
    }

    public void Hide()
    {
        IsVisible = false;
        _image.Source = null;
    }

    private static void AddAt(Grid grid, View view, int column, int row)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        grid.Children.Add(view);
    }

    private static Color ThemeColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Gray;

    private PageFrame PageArea() => PageFrame.Fit(_area.Width, _area.Height, _aspectRatio);

    private Border CreateHandle(CropHandle handle)
    {
        var isCorner = handle is CropHandle.TopLeft or CropHandle.TopRight or CropHandle.BottomLeft or CropHandle.BottomRight;
        var horizontalEdge = handle is CropHandle.Top or CropHandle.Bottom;

        var visual = new Border
        {
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 2,
            WidthRequest = isCorner ? 22 : horizontalEdge ? 36 : 7,
            HeightRequest = isCorner ? 22 : horizontalEdge ? 7 : 36,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = isCorner ? 11 : 4 }
        };
        visual.SetAppThemeColor(Border.BackgroundColorProperty, ThemeColor("PrimaryLight"), ThemeColor("PrimaryDark"));
        visual.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(Colors.White), new SolidColorBrush(Colors.Black));

        var touch = new Border
        {
            AutomationId = $"CropHandle_{handle}",
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            WidthRequest = TouchSize,
            HeightRequest = TouchSize,
            Content = visual
        };

        AttachDrag(touch, handle);
        return touch;
    }

    private void AttachDrag(Border touch, CropHandle handle)
    {
#if ANDROID
        // Raw screen coordinates: the handle moves under the finger while
        // dragged, which skews a PanGestureRecognizer's TotalX/TotalY.
        touch.HandlerChanged += (_, _) =>
        {
            if (touch.Handler?.PlatformView is global::Android.Views.View platformView)
            {
                var listener = new PiccoloReader.Platforms.Android.SingleDragTouchListener(
                    platformView.Context!,
                    onStarted: () => _dragStart = _crop,
                    onRunning: (dx, dy) => DragBy(handle, dx, dy),
                    onEnded: () => { });
                _listeners.Add(listener);
                platformView.SetOnTouchListener(listener);
            }
        };
#else
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started)
            {
                _dragStart = _crop;
            }
            else if (e.StatusType == GestureStatus.Running)
            {
                DragBy(handle, e.TotalX, e.TotalY);
            }
        };
        touch.GestureRecognizers.Add(pan);
#endif
    }

    // dx/dy are in dp since the drag began.
    private void DragBy(CropHandle handle, double dx, double dy)
    {
        var frame = PageArea();
        var pageWidth = frame.Width * _area.Width;
        var pageHeight = frame.Height * _area.Height;
        if (pageWidth <= 0 || pageHeight <= 0)
        {
            return;
        }

        _crop = _dragStart.Drag(handle, dx / pageWidth, dy / pageHeight);
        Refresh();
    }

    private Rect CropRect()
    {
        var frame = PageArea();
        var visible = frame.VisibleRegion(_crop);
        return new Rect(visible.X * _area.Width, visible.Y * _area.Height, visible.Width * _area.Width, visible.Height * _area.Height);
    }

    private void Refresh()
    {
        if (_area.Width <= 0 || _area.Height <= 0)
        {
            return;
        }

        var rect = CropRect();
        var edgeLengthH = Math.Clamp(rect.Width - TouchSize, TouchSize / 2, MaxEdgeTouchLength);
        var edgeLengthV = Math.Clamp(rect.Height - TouchSize, TouchSize / 2, MaxEdgeTouchLength);

        void Place(CropHandle handle, double centerX, double centerY, double width, double height)
        {
            var border = _handles[handle];
            border.WidthRequest = width;
            border.HeightRequest = height;
            border.TranslationX = centerX - width / 2;
            border.TranslationY = centerY - height / 2;
        }

        Place(CropHandle.Top, rect.Center.X, rect.Top, edgeLengthH, TouchSize);
        Place(CropHandle.Bottom, rect.Center.X, rect.Bottom, edgeLengthH, TouchSize);
        Place(CropHandle.Left, rect.Left, rect.Center.Y, TouchSize, edgeLengthV);
        Place(CropHandle.Right, rect.Right, rect.Center.Y, TouchSize, edgeLengthV);
        Place(CropHandle.TopLeft, rect.Left, rect.Top, TouchSize, TouchSize);
        Place(CropHandle.TopRight, rect.Right, rect.Top, TouchSize, TouchSize);
        Place(CropHandle.BottomLeft, rect.Left, rect.Bottom, TouchSize, TouchSize);
        Place(CropHandle.BottomRight, rect.Right, rect.Bottom, TouchSize, TouchSize);

        _canvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_area.Width <= 0 || _area.Height <= 0)
        {
            return;
        }

        var density = (float)(e.Info.Width / _area.Width);
        var rect = CropRect();
        var skRect = new SKRect(
            (float)rect.Left * density,
            (float)rect.Top * density,
            (float)rect.Right * density,
            (float)rect.Bottom * density);

        // Dim everything but the crop.
        canvas.Save();
        canvas.ClipRect(skRect, SKClipOperation.Difference);
        canvas.DrawColor(new SKColor(0, 0, 0, 140));
        canvas.Restore();

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var primary = ThemeColor(dark ? "PrimaryDark" : "PrimaryLight");
        using var border = new SKPaint
        {
            Color = new SKColor((byte)(primary.Red * 255), (byte)(primary.Green * 255), (byte)(primary.Blue * 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2 * density,
            IsAntialias = true
        };
        canvas.DrawRect(skRect, border);

        using var guide = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 110),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = density,
            IsAntialias = true
        };
        for (var i = 1; i <= 2; i++)
        {
            var x = skRect.Left + skRect.Width * i / 3;
            var y = skRect.Top + skRect.Height * i / 3;
            canvas.DrawLine(x, skRect.Top, x, skRect.Bottom, guide);
            canvas.DrawLine(skRect.Left, y, skRect.Right, y, guide);
        }
    }

    private View CreateBottomBar()
    {
        var reset = CreateButton(AppStrings.CropReset, "CropResetButton", filled: false, outlined: true, () =>
        {
            _crop = PageCrop.Full;
            Refresh();
        });
        var cancel = CreateButton(AppStrings.Cancel, "CropCancelButton", filled: false, outlined: false, () =>
        {
            Hide();
            Cancelled?.Invoke(this, EventArgs.Empty);
        });
        var apply = CreateButton(AppStrings.CropApply, "CropApplyButton", filled: true, outlined: false, () =>
        {
            var crop = _crop;
            Hide();
            Applied?.Invoke(this, crop);
        });

        var bar = new Grid
        {
            Margin = new Thickness(16, 8, 16, 16),
            ColumnSpacing = 8,
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        AddAt(bar, reset, 0, 0);
        AddAt(bar, cancel, 2, 0);
        AddAt(bar, apply, 3, 0);
        return bar;
    }

    private static Border CreateButton(string text, string automationId, bool filled, bool outlined, Action onTapped)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };

        var button = new Border
        {
            AutomationId = automationId,
            HeightRequest = 44,
            MinimumWidthRequest = 44,
            Padding = new Thickness(16, 0),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 },
            StrokeThickness = outlined ? 1 : 0,
            Content = label
        };
        SemanticProperties.SetDescription(button, text);

        if (filled)
        {
            button.SetAppThemeColor(Border.BackgroundColorProperty, ThemeColor("PrimaryLight"), ThemeColor("PrimaryDark"));
            label.SetAppThemeColor(Label.TextColorProperty, ThemeColor("OnPrimaryLight"), ThemeColor("OnPrimaryDark"));
        }
        else
        {
            button.BackgroundColor = Colors.Transparent;
            label.SetAppThemeColor(Label.TextColorProperty, ThemeColor("PrimaryLight"), ThemeColor("PrimaryDark"));
            if (outlined)
            {
                button.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(ThemeColor("OutlineLight")), new SolidColorBrush(ThemeColor("OutlineDark")));
            }
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTapped();
        button.GestureRecognizers.Add(tap);
        return button;
    }
}
