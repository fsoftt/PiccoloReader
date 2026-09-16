using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

[QueryProperty(nameof(SheetId), "sheetId")]
public partial class SheetViewerPage : ContentPage
{
    private const double ZoomedInThreshold = 1.05;
    private const double PageTurnDragThreshold = 60;
    private const double SelectionHandlePadding = 20;

    private readonly SheetViewerViewModel _viewModel;

    private double _startScale = 1;
    private double _currentScale = 1;
    private double _xOffset;
    private double _yOffset;
    private double _panTotalX;

    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _moveStartX;
    private double _moveStartY;

    private SKTypeface? _bravuraTypeface;

    public SheetViewerPage(SheetViewerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.CurrentPageAnnotations.CollectionChanged += (_, _) => AnnotationCanvas.InvalidateSurface();
    }

    public string SheetId { get; set; } = string.Empty;

    public IReadOnlyList<MusicIconCategory> IconCategories => MusicIconCatalog.Categories;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        await EnsureBravuraTypefaceLoadedAsync();

        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
        var targetWidthPx = (int)displayInfo.Width;
        var targetHeightPx = (int)displayInfo.Height;

        await _viewModel.LoadAsync(sheetId, targetWidthPx, targetHeightPx);
        ResetZoom();
        AnnotationCanvas.InvalidateSurface();
    }

    private void ResetZoom()
    {
        _startScale = 1;
        _currentScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        PageContainer.Scale = 1;
        PageContainer.TranslationX = 0;
        PageContainer.TranslationY = 0;
        UpdateSelectionOverlay();
    }

    // AnnotationCanvas fully covers PageContainer (it's stacked on top of
    // PageImage in the same Grid cell), so it receives every tap before
    // PageContainer's own gesture recognizers would - a plain
    // TapGestureRecognizer on PageContainer here would just never fire.
    // Rather than stack two competing tap recognizers (the same class of
    // gesture-arena conflict already hit with Pan+Swipe in Plan 2), all
    // tap handling - tool panel dismissal, icon hit-testing/selection, and
    // the fallback tap-to-turn-page - lives in this one handler.
    private void OnAnnotationCanvasTapped(object? sender, TappedEventArgs e)
    {
        if (ToolPanel.IsVisible)
        {
            ToolPanel.IsVisible = false;
            return;
        }

        var position = e.GetPosition(PageContainer);
        if (position is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        var normalizedX = position.Value.X / PageContainer.Width;
        var normalizedY = position.Value.Y / PageContainer.Height;

        var hit = _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
            normalizedX >= a.X && normalizedX <= a.X + a.Width &&
            normalizedY >= a.Y && normalizedY <= a.Y + a.Height);

        if (hit is not null)
        {
            _viewModel.SelectedAnnotation = hit;
            UpdateSelectionOverlay();
            return;
        }

        if (_viewModel.SelectedAnnotation is not null)
        {
            _viewModel.SelectedAnnotation = null;
            UpdateSelectionOverlay();
            return;
        }

        if (_currentScale > ZoomedInThreshold)
        {
            // Ignore tap-to-turn while zoomed in - the user is most likely
            // trying to look around the page, not turn it.
            return;
        }

        if (position.Value.X < PageContainer.Width / 2)
        {
            TryGoToPreviousPage();
        }
        else
        {
            TryGoToNextPage();
        }
    }

    // Positions/sizes SelectionOverlay over the selected annotation using
    // the same normalized-coordinate convention as everywhere else -
    // SelectionOverlay is anchored Start/Start in PageContainer's Grid
    // cell, so TranslationX/Y (plus WidthRequest/HeightRequest) place it
    // exactly like PageContainer's own transform positions the page.
    //
    // SelectionOverlay is padded by SelectionHandlePadding beyond the
    // icon's own box on every side, and SelectionBorder is inset by the
    // same amount (Margin="20" in XAML) so it still lines up exactly with
    // the box - confirmed empirically on-device: without this padding,
    // ResizeHandle/DeleteButton (translated outside the box to sit at its
    // corners) render correctly but never receive taps, because a parent
    // ViewGroup's touch dispatch tests a child against its own arranged
    // bounds, not the visual union of where its children overflow to.
    private void UpdateSelectionOverlay()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            SelectionOverlay.IsVisible = false;
            return;
        }

        SelectionOverlay.IsVisible = true;

        var left = annotation.X * PageContainer.Width;
        var top = annotation.Y * PageContainer.Height;
        var width = annotation.Width * PageContainer.Width;
        var height = annotation.Height * PageContainer.Height;

        SelectionOverlay.WidthRequest = width + SelectionHandlePadding * 2;
        SelectionOverlay.HeightRequest = height + SelectionHandlePadding * 2;
        SelectionOverlay.TranslationX = left - SelectionHandlePadding;
        SelectionOverlay.TranslationY = top - SelectionHandlePadding;

        ResizeHandle.TranslationX = SelectionHandlePadding + width - ResizeHandle.WidthRequest / 2;
        ResizeHandle.TranslationY = SelectionHandlePadding + height - ResizeHandle.HeightRequest / 2;

        DeleteButton.TranslationX = SelectionHandlePadding + width - DeleteButton.WidthRequest / 2;
        DeleteButton.TranslationY = SelectionHandlePadding - DeleteButton.HeightRequest / 2;
    }

    private void OnSelectionMovePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _moveStartX = annotation.X;
                _moveStartY = annotation.Y;
                break;

            case GestureStatus.Running:
                annotation.X = _moveStartX + e.TotalX / PageContainer.Width;
                annotation.Y = _moveStartY + e.TotalY / PageContainer.Height;
                UpdateSelectionOverlay();
                AnnotationCanvas.InvalidateSurface();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _ = _viewModel.MoveSelectedAnnotationAsync(annotation.X, annotation.Y);
                break;
        }
    }

    private void OnSelectionResizePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _resizeStartWidth = annotation.Width;
                _resizeStartHeight = annotation.Height;
                break;

            case GestureStatus.Running:
                annotation.Width = Math.Max(0.02, _resizeStartWidth + e.TotalX / PageContainer.Width);
                annotation.Height = Math.Max(0.02, _resizeStartHeight + e.TotalY / PageContainer.Height);
                UpdateSelectionOverlay();
                AnnotationCanvas.InvalidateSurface();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _ = _viewModel.ResizeSelectedAnnotationAsync(annotation.Width, annotation.Height);
                break;
        }
    }

    private async void OnDeleteSelectedIconClicked(object? sender, EventArgs e)
    {
        await _viewModel.DeleteSelectedAnnotationCommand.ExecuteAsync(null);
        UpdateSelectionOverlay();
    }

    // Pinch-to-zoom. On Android, PinchGestureHandler.OnPinch (MAUI source,
    // src/Controls/src/Core/Platform/Android/PinchGestureHandler.cs)
    // already computes e.Scale as `1 + (rawFrameDelta - 1) * viewScaleAtGestureStart`
    // before it ever reaches this handler - i.e. e.Scale-1 is already
    // scaled by the view's starting scale. Multiplying by _startScale
    // again here (as an earlier version of this code did, copying a
    // formula meant for a raw/unscaled delta) squares that factor:
    // harmless at scale=1 (1*1=1, why zoom-in "looked fine" from a fresh
    // page), but wildly overcorrects once already zoomed in - which is
    // why zooming back out was broken. Only the *delta* needs adding, no
    // extra multiplication.
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = PageContainer.Scale;
            PageContainer.AnchorX = 0;
            PageContainer.AnchorY = 0;
        }
        else if (e.Status == GestureStatus.Running)
        {
            _currentScale += e.Scale - 1;
            _currentScale = Math.Max(1, _currentScale);

            var renderedX = PageContainer.X + _xOffset;
            var deltaX = renderedX / PageContainer.Width;
            var deltaWidth = PageContainer.Width / (PageContainer.Width * _startScale);
            var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

            var renderedY = PageContainer.Y + _yOffset;
            var deltaY = renderedY / PageContainer.Height;
            var deltaHeight = PageContainer.Height / (PageContainer.Height * _startScale);
            var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

            var targetX = _xOffset - (originX * PageContainer.Width * (_currentScale - _startScale));
            var targetY = _yOffset - (originY * PageContainer.Height * (_currentScale - _startScale));

            PageContainer.TranslationX = Math.Clamp(targetX, -PageContainer.Width * (_currentScale - 1), 0);
            PageContainer.TranslationY = Math.Clamp(targetY, -PageContainer.Height * (_currentScale - 1), 0);
            PageContainer.Scale = _currentScale;
        }
        else if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _xOffset = PageContainer.TranslationX;
            _yOffset = PageContainer.TranslationY;
        }
    }

    // Handles both panning around a zoomed-in page and swipe-to-turn-pages
    // when not zoomed. A separate SwipeGestureRecognizer on the same
    // element was removed - stacking Pan and Swipe recognizers on one
    // view is a known source of gesture-arena conflicts on Android (Pan
    // claims the touch as soon as it moves, before Swipe's own threshold
    // logic gets a chance to recognize the gesture), which is why swiping
    // to turn pages wasn't working.
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (ToolPanel.IsVisible)
        {
            // Dragging on the page while the tool panel is open closes it,
            // same as a plain tap (see OnAnnotationCanvasTapped) - don't
            // also pan/turn the page underneath.
            if (e.StatusType == GestureStatus.Completed)
            {
                ToolPanel.IsVisible = false;
            }

            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                if (_currentScale > ZoomedInThreshold)
                {
                    PageContainer.TranslationX = Math.Clamp(_xOffset + e.TotalX, -PageContainer.Width * (_currentScale - 1), 0);
                    PageContainer.TranslationY = Math.Clamp(_yOffset + e.TotalY, -PageContainer.Height * (_currentScale - 1), 0);
                }

                _panTotalX = e.TotalX;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_currentScale > ZoomedInThreshold)
                {
                    _xOffset = PageContainer.TranslationX;
                    _yOffset = PageContainer.TranslationY;
                }
                else if (_panTotalX <= -PageTurnDragThreshold)
                {
                    TryGoToNextPage();
                }
                else if (_panTotalX >= PageTurnDragThreshold)
                {
                    TryGoToPreviousPage();
                }

                _panTotalX = 0;
                break;
        }
    }

    private void TryGoToNextPage()
    {
        if (_viewModel.NextPageCommand.CanExecute(null))
        {
            _viewModel.NextPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void TryGoToPreviousPage()
    {
        if (_viewModel.PreviousPageCommand.CanExecute(null))
        {
            _viewModel.PreviousPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private async void OnToggleToolPanelClicked(object? sender, EventArgs e)
    {
        if (!ToolPanel.IsVisible)
        {
            // Load before showing, not after - the icon buttons' SKCanvasViews
            // paint as soon as they're visible, and painting before the
            // typeface is ready would leave them blank with no later
            // repaint trigger.
            await EnsureBravuraTypefaceLoadedAsync();
        }

        ToolPanel.IsVisible = !ToolPanel.IsVisible;
    }

    private async Task EnsureBravuraTypefaceLoadedAsync()
    {
        if (_bravuraTypeface is not null)
        {
            return;
        }

        using var stream = await FileSystem.OpenAppPackageFileAsync("Bravura.otf");
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        _bravuraTypeface = SKTypeface.FromStream(memoryStream);
    }

    private async void OnIconPickerTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string iconKey)
        {
            await _viewModel.PlaceIconCommand.ExecuteAsync(iconKey);
            UpdateSelectionOverlay();
        }
    }

    private void OnIconGlyphPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (sender is not SKCanvasView { BindingContext: MusicIcon icon })
        {
            return;
        }

        var info = e.Info;
        DrawGlyphFitted(canvas, icon.Codepoint, new SKRect(0, 0, info.Width, info.Height));
    }

    private void OnAnnotationCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;

        foreach (var annotation in _viewModel.CurrentPageAnnotations)
        {
            var icon = MusicIconCatalog.FindByKey(annotation.IconKey);
            if (icon is null)
            {
                continue;
            }

            var targetRect = new SKRect(
                (float)(annotation.X * info.Width),
                (float)(annotation.Y * info.Height),
                (float)((annotation.X + annotation.Width) * info.Width),
                (float)((annotation.Y + annotation.Height) * info.Height));

            DrawGlyphFitted(canvas, icon.Codepoint, targetRect);
        }
    }

    // Fits a glyph tightly to targetRect instead of relying on the font's
    // own em-box sizing - Bravura (like most music fonts) reserves a lot
    // of vertical em-box space for staff-line alignment around each
    // glyph's actual ink, so naively scaling a font size leaves the
    // *displayed* glyph the same apparent size regardless (confirmed by
    // testing FontImageSource.Size 28 through 72 on a clean install with
    // zero visible difference). Measuring the glyph's own ink bounds via
    // Skia and fitting *that* to targetRect sidesteps it entirely.
    // Shared by the icon picker buttons and the on-page annotation
    // overlay so both render identically.
    private void DrawGlyphFitted(SKCanvas canvas, int codepoint, SKRect targetRect)
    {
        if (_bravuraTypeface is null || targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return;
        }

        var text = char.ConvertFromUtf32(codepoint);

        using var measureFont = new SKFont(_bravuraTypeface, 100);
        measureFont.MeasureText(text, out var measuredBounds);

        if (measuredBounds.Width <= 0 || measuredBounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(targetRect.Width / measuredBounds.Width, targetRect.Height / measuredBounds.Height) * 0.9f;
        using var font = new SKFont(_bravuraTypeface, 100f * scale);
        font.MeasureText(text, out var fittedBounds);

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

        var x = targetRect.MidX - fittedBounds.MidX;
        var y = targetRect.MidY - fittedBounds.MidY;

        canvas.DrawText(text, x, y, font, paint);
    }
}
