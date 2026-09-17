using CommunityToolkit.Maui.Core;
using PiccoloReader.Core.Data.Models;
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
    private const double TrashHitTestRadius = 60;

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

    private bool _isToolbarVisible = true;

    private SKTypeface? _bravuraTypeface;
    private readonly Dictionary<int, SKRect> _glyphBoundsCache = new();
    private readonly SKPaint _glyphPaint = new() { Color = SKColors.Black, IsAntialias = true };

    // Both computed per-call, not static fields, so they reflect the
    // theme at the moment each tab redraw happens. Active uses the same
    // Primary/PrimaryDark pairing the Library page already uses for
    // icons - plain Primary (#512BD4) reads fine on the light panel
    // background but nearly disappears against the dark one (Gray600,
    // #404040), so dark mode swaps to PrimaryDark (#ac99ea), a lighter
    // tint meant for exactly this case. Inactive uses dark grey against
    // the light panel and white against the dark one.
    private static Color TabActiveColor =>
        Application.Current!.RequestedTheme == AppTheme.Dark
            ? (Color)Application.Current!.Resources["PrimaryDark"]
            : (Color)Application.Current!.Resources["Primary"];

    private static Color TabInactiveIconColor =>
        Application.Current!.RequestedTheme == AppTheme.Dark
            ? Colors.White
            : (Color)Application.Current!.Resources["Gray600"];

    public SheetViewerPage(SheetViewerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.CurrentPageAnnotations.CollectionChanged += (_, _) => AnnotationCanvas.InvalidateSurface();

        // ToolbarItem has no bindable IsVisible in this MAUI version (it
        // derives from Element, not VisualElement), so visibility is
        // managed by adding/removing it from ToolbarItems instead - starts
        // removed since ActiveTool defaults to MusicIcons.
        ToolbarItems.Remove(DeactivateToolItem);
    }

    public string SheetId { get; set; } = string.Empty;

    public IReadOnlyList<MusicIconCategory> IconCategories => MusicIconCatalog.Categories;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        SetToolbarVisible(false);

        if (!int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        await EnsureBravuraTypefaceLoadedAsync();
        UpdateToolSections();

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

    // Tap lives on PageContainer itself, alongside its own Pinch and Pan
    // recognizers - not on AnnotationCanvas, even though AnnotationCanvas
    // is what visually sits on top and is the natural place to hit-test
    // icons. Confirmed on-device (PR #35 follow-up): a GestureRecognizer
    // on a child consumes the whole native touch stream for that gesture
    // on Android, so a *different* recognizer type on an ancestor never
    // gets ACTION_MOVE/ACTION_UP once a covering child's recognizer has
    // claimed ACTION_DOWN - even though nothing here stacks two
    // recognizers of the *same* type (the Pan+Swipe conflict from Plan 2).
    // Putting Tap on PageContainer instead - the same element Pinch and
    // Pan already live on, a combination already proven to coexist since
    // Plan 2 - avoids the cross-element conflict entirely. All tap
    // handling (tool panel dismissal, icon hit-testing/selection, and the
    // fallback tap-to-turn-page) stays in this one handler.
    // A tap now drives the toolbar's show/hide toggle instead of turning
    // the page - page turning stays available via swipe (see
    // PageTurnDragThreshold in OnPanUpdated), matching how most PDF/ebook
    // readers separate "reveal chrome" (tap) from "navigate" (swipe). The
    // very first tap while the toolbar is hidden only reveals it and does
    // nothing else - it doesn't also fall through to icon
    // selection/deselection - so a user reaching for the toolbar never
    // accidentally selects whatever happens to be under their thumb.
    private void OnPageContainerTapped(object? sender, TappedEventArgs e)
    {
        if (!_isToolbarVisible)
        {
            SetToolbarVisible(true);
            return;
        }

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

        // No tool is active here - while Pencil/Eraser is active, the
        // relevant DrawingView covers PageContainer and captures the touch
        // stream itself (see UpdateToolSections), so this handler never
        // runs in that state. That's what satisfies "hide again only when
        // no tool is selected" - there's no extra check needed for it.
        SetToolbarVisible(false);
    }

    // Toggling Shell.SetNavBarIsVisible was tried first and rejected -
    // confirmed on-device that Android doesn't re-run window-inset
    // dispatch when the nav bar is dynamically re-shown, so the whole bar
    // rendered up under the status bar every time (title, back button and
    // icons all overlapping the clock/battery icons) after the first
    // hide/show cycle. Toggling the icon ToolbarItems in and out of the
    // collection instead - the same technique DeactivateToolItem already
    // uses for its own visibility (ToolbarItem has no bindable IsVisible in
    // this MAUI version) - avoids that renderer bug entirely, at the cost
    // of leaving the title/back button always visible; only the tool icons
    // and the page indicator hide/show with this toggle.
    private void SetToolbarVisible(bool visible)
    {
        _isToolbarVisible = visible;
        PageIndicatorLabel.IsVisible = visible;

        if (visible)
        {
            AddToolbarItemIfMissing(ToolPanelToggleItem);
            AddToolbarItemIfMissing(BookmarksItem);
            if (_viewModel.IsDrawingToolActive)
            {
                AddToolbarItemIfMissing(DeactivateToolItem);
            }
        }
        else
        {
            ToolbarItems.Remove(ToolPanelToggleItem);
            ToolbarItems.Remove(BookmarksItem);
            ToolbarItems.Remove(DeactivateToolItem);
        }
    }

    private void AddToolbarItemIfMissing(ToolbarItem item)
    {
        if (!ToolbarItems.Contains(item))
        {
            ToolbarItems.Add(item);
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
    // ResizeHandle (translated outside the box to sit at its corner)
    // renders correctly but never receives taps, because a parent
    // ViewGroup's touch dispatch tests a child against its own arranged
    // bounds, not the visual union of where its children overflow to.
    private void UpdateSelectionOverlay()
    {
        RepositionSelectionOverlay();

        // A fresh selection (new tap, newly placed icon) always starts
        // with both controls visible. Repositioning alone - the per-frame
        // case during an active drag - must NOT reach this: it would
        // immediately re-show the controls SetDragControlsVisible(false)
        // just hid on GestureStatus.Started, on the very next Running
        // frame. That's why the drag handlers below call
        // RepositionSelectionOverlay() directly instead of this method.
        SetDragControlsVisible(_viewModel.SelectedAnnotation is not null);
    }

    private void RepositionSelectionOverlay()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            SelectionOverlay.IsVisible = false;
            TrashTarget.IsVisible = false;
            return;
        }

        SelectionOverlay.IsVisible = true;
        TrashTarget.IsVisible = true;

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
        UpdateResizeHandleScale();
    }

    // ResizeHandle lives inside PageContainer, so without this it would
    // visually scale right along with pinch-zoom - at high zoom a 24px
    // handle can render 3-4x larger on screen, easily growing bigger than
    // the icon itself and hiding it (the same problem the old corner
    // delete button had, which is why deletion moved to the fixed-size
    // TrashTarget instead of trying to counter-scale a button too). A
    // Scale inverse to PageContainer's own keeps it a constant on-screen
    // size at any zoom level; TranslationX/Y above already position its
    // center at the box corner independent of its own Scale, so this can
    // be set without touching that math. Called continuously during an
    // active pinch too (see OnPinchUpdated), not just on
    // selection/reposition, so the handle doesn't visibly grow mid-pinch.
    private void UpdateResizeHandleScale()
    {
        ResizeHandle.Scale = _currentScale > 0 ? 1.0 / _currentScale : 1.0;
    }

    // Hides the resize handle while a move/resize drag is in progress,
    // leaving only the thin border outline - reported feedback was that
    // positioning an icon precisely under a note while zoomed in was
    // impossible because a visible handle sat right on top of the note
    // being aligned to. It doesn't need to stay visible mid-drag: the
    // handle's own drag keeps tracking touch input whether or not it's
    // drawn, and the border alone is enough spatial feedback for the move
    // drag. Reappears the instant the drag ends. The delete affordance no
    // longer lives on the overlay at all (see TrashTarget), so there's
    // nothing else here to hide.
    private void SetDragControlsVisible(bool visible)
    {
        ResizeHandle.IsVisible = visible;
    }

    private async void OnSelectionMovePanUpdated(object? sender, PanUpdatedEventArgs e)
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
                SetDragControlsVisible(false);
                break;

            case GestureStatus.Running:
                // e.TotalX/TotalY are raw screen-pixel deltas, unaffected
                // by PageContainer's own zoom Scale (confirmed on a real
                // device: a modest finger drag while zoomed in produced a
                // wildly larger move/resize than the finger's visual
                // travel suggested, and got jumpier/more "shaky" the more
                // zoomed in the page was - both are the signature of a
                // screen-pixel delta being normalized against the page's
                // *unscaled* width/height without first dividing out the
                // zoom factor). At zoom S, the same screen-pixel drag
                // covers 1/S as much of the page, so the delta must be
                // scaled down by _currentScale before normalizing -
                // dividing by PageContainer.Width*_currentScale (the
                // page's actual on-screen size) instead of just
                // PageContainer.Width (its unscaled size) does that in
                // one step. At the default zoom (_currentScale == 1) this
                // is identical to the previous formula.
                annotation.X = _moveStartX + e.TotalX / (PageContainer.Width * _currentScale);
                annotation.Y = _moveStartY + e.TotalY / (PageContainer.Height * _currentScale);
                RepositionSelectionOverlay();
                AnnotationCanvas.InvalidateSurface();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SetDragControlsVisible(true);
                if (IsOverTrashTarget(annotation))
                {
                    // Must be awaited, not fire-and-forget, before
                    // UpdateSelectionOverlay() runs - otherwise the
                    // overlay reads SelectedAnnotation's still-live
                    // (pre-delete) state and repositions itself at the
                    // drop point instead of hiding, leaving a stale
                    // border/handle behind once the delete actually lands
                    // a moment later (confirmed on-device: the icon
                    // itself vanished from the page - the delete worked -
                    // but its selection border stayed stuck at the drop
                    // point).
                    await _viewModel.DeleteSelectedAnnotationCommand.ExecuteAsync(null);
                    UpdateSelectionOverlay();
                }
                else
                {
                    _ = _viewModel.MoveSelectedAnnotationAsync(annotation.X, annotation.Y);
                }
                break;
        }
    }

    // Whether the selected icon's current center - converted from
    // PageContainer's local/zoomed coordinate space into the same
    // page-relative space TrashTarget lives in - falls within (a generous
    // tolerance around) TrashTarget, i.e. whether the icon was "dropped
    // on the trash" at the end of a move drag. PageContainer.X/Y are its
    // position within the outer Grid, which fills the page with no offset
    // of its own, so they're already page-relative; composing them with
    // PageContainer's own zoom Translation/Scale (anchored at its own
    // top-left - see OnPinchUpdated) gives the icon's true on-screen
    // position at any zoom level, in the same coordinate space
    // TrashTarget's own X/Y/Width/Height are already expressed in (it's a
    // direct sibling of PageContainer, not a descendant, so it's never
    // affected by the zoom transform).
    private bool IsOverTrashTarget(Annotation annotation)
    {
        if (!TrashTarget.IsVisible || TrashTarget.Width <= 0 || TrashTarget.Height <= 0)
        {
            return false;
        }

        var localCenterX = (annotation.X + annotation.Width / 2) * PageContainer.Width;
        var localCenterY = (annotation.Y + annotation.Height / 2) * PageContainer.Height;

        var screenCenterX = PageContainer.X + PageContainer.TranslationX + localCenterX * _currentScale;
        var screenCenterY = PageContainer.Y + PageContainer.TranslationY + localCenterY * _currentScale;

        var trashCenterX = TrashTarget.X + TrashTarget.Width / 2;
        var trashCenterY = TrashTarget.Y + TrashTarget.Height / 2;

        var dx = screenCenterX - trashCenterX;
        var dy = screenCenterY - trashCenterY;
        var radius = TrashTarget.Width / 2 + TrashHitTestRadius;

        return dx * dx + dy * dy <= radius * radius;
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
                SetDragControlsVisible(false);
                break;

            case GestureStatus.Running:
                // See the matching comment in OnSelectionMovePanUpdated -
                // e.TotalX/TotalY are raw screen pixels, so they need
                // dividing by _currentScale (via PageContainer.Width/Height
                // * _currentScale, its actual on-screen size) before
                // normalizing, or a resize drag while zoomed in ends up
                // several times larger than the finger's own travel.
                annotation.Width = Math.Max(0.02, _resizeStartWidth + e.TotalX / (PageContainer.Width * _currentScale));
                annotation.Height = Math.Max(0.02, _resizeStartHeight + e.TotalY / (PageContainer.Height * _currentScale));
                RepositionSelectionOverlay();
                AnnotationCanvas.InvalidateSurface();
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SetDragControlsVisible(true);
                _ = _viewModel.ResizeSelectedAnnotationAsync(annotation.Width, annotation.Height);
                break;
        }
    }

    private async void OnTrashTargetTapped(object? sender, TappedEventArgs e)
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
            UpdateResizeHandleScale();
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
            // same as a plain tap (see OnPageContainerTapped) - don't
            // also pan/turn the page underneath.
            if (e.StatusType == GestureStatus.Completed)
            {
                ToolPanel.IsVisible = false;
            }

            return;
        }

        // While Eraser is active, EraserDrawingView covers PageContainer
        // and captures the entire touch stream for drag-to-erase (see its
        // handlers below) the same way PencilDrawingView already does for
        // drawing - this handler shouldn't normally even run during an
        // Eraser drag, but bails out just in case, rather than letting a
        // stray event zoom-pan or turn the page mid-erase.
        if (_viewModel.ActiveTool == AnnotationTool.Eraser)
        {
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

    // Shows the eraser's hit-test radius centered on the current touch
    // point, hiding itself 350ms after the most recent call - during a
    // drag (see the EraserDrawingView handlers below), each new point
    // restarts the timer, so the indicator tracks the finger live and
    // only fades out shortly after it lifts.
    private void ShowEraserRadiusIndicator(double normalizedX, double normalizedY)
    {
        var radiusPx = _viewModel.EraserRadius * PageContainer.Width;
        EraserRadiusIndicator.WidthRequest = radiusPx * 2;
        EraserRadiusIndicator.HeightRequest = radiusPx * 2;
        EraserRadiusIndicator.TranslationX = normalizedX * PageContainer.Width - radiusPx;
        EraserRadiusIndicator.TranslationY = normalizedY * PageContainer.Height - radiusPx;
        EraserRadiusIndicator.IsVisible = true;

        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(350), () =>
        {
            EraserRadiusIndicator.IsVisible = false;
            return false;
        });
    }

    private void EraseAt(double normalizedX, double normalizedY)
    {
        ShowEraserRadiusIndicator(normalizedX, normalizedY);

        var radius = _viewModel.EraserRadius;
        var hit = _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
        {
            if (a.IsStroke)
            {
                var points = AnnotationService.DeserializePoints(a.Points);
                return StrokeHitTester.DistanceToPolyline(normalizedX, normalizedY, points) <= radius;
            }

            return normalizedX >= a.X - radius && normalizedX <= a.X + a.Width + radius &&
                   normalizedY >= a.Y - radius && normalizedY <= a.Y + a.Height + radius;
        });

        if (hit is not null)
        {
            _ = _viewModel.EraseAnnotationAsync(hit);
            AnnotationCanvas.InvalidateSurface();
        }
    }

    // EraserDrawingView gives real drag-to-erase, unlike the
    // PointerGestureRecognizer approach tried first: confirmed on-device
    // that PointerGestureRecognizer only fires PointerReleased for touch
    // input on Android, never PointerPressed/PointerMoved, so there was no
    // way to track a drag path through it (Eraser originally shipped as
    // tap-to-erase because of this). DrawingView doesn't have that gap -
    // it drives its own native touch handling directly (confirmed working
    // for Pencil), and DrawingLineStarted/PointDrawn between them cover
    // both the initial touch-down point and every subsequent point along
    // the drag, in the same PageContainer-local coordinate space Pencil's
    // points already use. EraserDrawingView never persists anything - it's
    // repurposed purely as a reliable continuous-touch-position source,
    // hit-testing and deleting live via EraseAt at every point.
    private void OnEraserDrawingLineStarted(object? sender, DrawingLineStartedEventArgs e)
    {
        EraseAtDrawingViewPoint(e.Point);
    }

    private void OnEraserPointDrawn(object? sender, PointDrawnEventArgs e)
    {
        EraseAtDrawingViewPoint(e.Point);
    }

    private void OnEraserDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        EraserRadiusIndicator.IsVisible = false;
        ToolPanel.IsVisible = false;
    }

    private void OnEraserDrawingLineCancelled(object? sender, EventArgs e)
    {
        EraserRadiusIndicator.IsVisible = false;
    }

    private void EraseAtDrawingViewPoint(PointF point)
    {
        if (PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        EraseAt(point.X / PageContainer.Width, point.Y / PageContainer.Height);
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

    private async void OnPageIndicatorTapped(object? sender, TappedEventArgs e)
    {
        var input = await DisplayPromptAsync(
            "Go to Page",
            $"Enter a page number (1-{_viewModel.PageCount}):",
            initialValue: _viewModel.CurrentPageDisplay.ToString(),
            keyboard: Keyboard.Numeric);

        if (input is null)
        {
            return;
        }

        if (int.TryParse(input, out var pageNumber) && pageNumber >= 1 && pageNumber <= _viewModel.PageCount)
        {
            await _viewModel.GoToPageAsync(pageNumber - 1);
            ResetZoom();
        }
    }

    // Single entry point for bookmarks, per explicit request - no separate
    // "+" toolbar icon. "Add Bookmark" is always the last option in the
    // same action sheet as the existing bookmarks, so the one button both
    // lists what's there and lets you add to it.
    private async void OnBookmarksClicked(object? sender, EventArgs e)
    {
        const string addBookmarkOption = "Add Bookmark";

        var ordered = _viewModel.Bookmarks.OrderBy(b => b.PageIndex).ToList();
        var options = ordered.Select(b => $"Page {b.PageIndex + 1}").Append(addBookmarkOption).ToArray();

        var choice = await DisplayActionSheetAsync("Bookmarks", "Cancel", null, options);

        if (choice is null || choice == "Cancel")
        {
            return;
        }

        if (choice == addBookmarkOption)
        {
            await AddBookmarkAsync();
            return;
        }

        var index = Array.IndexOf(options, choice);
        if (index >= 0 && index < ordered.Count)
        {
            await _viewModel.GoToPageAsync(ordered[index].PageIndex);
            ResetZoom();
        }
    }

    private async Task AddBookmarkAsync()
    {
        var input = await DisplayPromptAsync(
            "Add Bookmark",
            $"Page number (1-{_viewModel.PageCount}):",
            initialValue: _viewModel.CurrentPageDisplay.ToString(),
            keyboard: Keyboard.Numeric);

        if (input is null)
        {
            return;
        }

        if (int.TryParse(input, out var pageNumber) && pageNumber >= 1 && pageNumber <= _viewModel.PageCount)
        {
            await _viewModel.AddBookmarkAsync(pageNumber - 1);
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

    private void OnToolTabTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string toolName || !Enum.TryParse<AnnotationTool>(toolName, out var tool))
        {
            return;
        }

        _viewModel.ActiveTool = tool;
        UpdateToolSections();
    }

    private void OnDeactivateToolClicked(object? sender, EventArgs e)
    {
        _viewModel.ActiveTool = AnnotationTool.MusicIcons;
        UpdateToolSections();
    }

    private void UpdateToolSections()
    {
        MusicIconsSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.MusicIcons;
        PencilSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Pencil;
        EraserSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Eraser;

        SetTabAppearance(MusicIconsTabIcon, _viewModel.ActiveTool == AnnotationTool.MusicIcons);
        SetTabAppearance(PencilTabIcon, _viewModel.ActiveTool == AnnotationTool.Pencil);
        SetTabAppearance(EraserTabIcon, _viewModel.ActiveTool == AnnotationTool.Eraser);

        if (_viewModel.IsDrawingToolActive && _isToolbarVisible)
        {
            AddToolbarItemIfMissing(DeactivateToolItem);
        }
        else
        {
            ToolbarItems.Remove(DeactivateToolItem);
        }

        // InputTransparent is left False permanently in XAML (never toggled
        // here) rather than True/False alongside IsVisible - confirmed
        // on-device that touch capture stopped working entirely as soon as
        // ZIndex was also set on PencilDrawingView to force it above its
        // siblings (which also broke its own Transparent BackgroundColor,
        // rendering solid black). PencilDrawingView is already the last
        // child in PageContainer's XAML, which is enough for correct
        // z-order without ZIndex - don't add one.
        var pencilActive = _viewModel.ActiveTool == AnnotationTool.Pencil;
        PencilDrawingView.IsVisible = pencilActive;
        if (pencilActive)
        {
            PencilDrawingView.LineColor = Color.FromArgb(_viewModel.PencilColorHex);
            UpdatePencilDrawingViewLineWidth();
            UpdatePencilColorSwatchSelection();
            PencilWidthPreview.InvalidateSurface();
        }

        var eraserActive = _viewModel.ActiveTool == AnnotationTool.Eraser;
        EraserDrawingView.IsVisible = eraserActive;
        if (eraserActive)
        {
            UpdateEraserDrawingViewLineWidth();
        }
    }

    private void OnEraserRadiusChanged(object? sender, ValueChangedEventArgs e)
    {
        UpdateEraserDrawingViewLineWidth();
    }

    // The swipe trajectory line is just EraserDrawingView's own rendered
    // path (never persisted - see EraseAt), so its default LineWidth reads
    // as a thick, fixed-size stroke unrelated to how big an area is
    // actually being erased. Scaling it off EraserRadius (0.02-0.08) onto a
    // small 2-8dp range keeps it visibly thin while still tracking the
    // selected eraser size, rather than tying it to the same page-width
    // pixel conversion Pencil uses - that would make the trajectory line
    // itself as wide as the eraser's hit-test radius, which is meant to be
    // a generous forgiving target, not a thin line.
    private void UpdateEraserDrawingViewLineWidth()
    {
        EraserDrawingView.LineWidth = (float)(_viewModel.EraserRadius * 100);
    }

    private static void SetTabAppearance(FontImageSource icon, bool active)
    {
        icon.Color = active ? TabActiveColor : TabInactiveIconColor;
    }

    // Highlights the ring around the swatch matching the current pencil
    // color and clears the rest - a small drop shadow on the selected ring
    // gives it a slight "lift" off the panel, on top of the stroke ring.
    private void UpdatePencilColorSwatchSelection()
    {
        SetColorRingSelected(ColorRingBlack, "#000000");
        SetColorRingSelected(ColorRingRed, "#FF0000");
        SetColorRingSelected(ColorRingBlue, "#0000FF");
        SetColorRingSelected(ColorRingGreen, "#008000");
        SetColorRingSelected(ColorRingOrange, "#FFA500");
    }

    private void SetColorRingSelected(Border ring, string colorHex)
    {
        var isSelected = string.Equals(_viewModel.PencilColorHex, colorHex, StringComparison.OrdinalIgnoreCase);
        ring.Stroke = isSelected ? (Color)Application.Current!.Resources["Primary"] : Colors.Transparent;
        ring.Shadow = isSelected
            ? new Shadow { Brush = Colors.Black, Opacity = 0.3f, Radius = 6, Offset = new Point(0, 2) }
            : null!;
    }

    private void OnPencilColorTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string colorHex)
        {
            _viewModel.PencilColorHex = colorHex;
            PencilDrawingView.LineColor = Color.FromArgb(colorHex);
            UpdatePencilColorSwatchSelection();
            PencilWidthPreview.InvalidateSurface();
        }
    }

    private void OnPencilWidthChanged(object? sender, ValueChangedEventArgs e)
    {
        UpdatePencilDrawingViewLineWidth();
        PencilWidthPreview.InvalidateSurface();
    }

    private void UpdatePencilDrawingViewLineWidth()
    {
        if (PageContainer.Width > 0)
        {
            PencilDrawingView.LineWidth = (float)(_viewModel.PencilStrokeWidth * PageContainer.Width);
        }
    }

    // Draws an S-curve instead of a straight bar - a stroke preview reads
    // more like an actual pencil mark when it curves, and it's a better
    // showcase of StrokeCap.Round at small widths than a straight line's
    // flat ends. PencilStrokeWidth's slider range is 0.003-0.02, mapped to
    // the same 3-40 device-independent-pixel stroke width the old bar's
    // height used, so the preview stays comparable to before.
    private void OnPencilWidthPreviewPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;
        var strokeWidth = (float)Math.Clamp(_viewModel.PencilStrokeWidth * 2000, 3, 40);

        using var path = new SKPath();
        var margin = info.Width * 0.08f;
        var midY = info.Height / 2f;
        path.MoveTo(margin, midY);
        path.CubicTo(
            info.Width * 0.35f, midY - info.Height * 0.35f,
            info.Width * 0.65f, midY + info.Height * 0.35f,
            info.Width - margin, midY);

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(_viewModel.PencilColorHex),
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };

        canvas.DrawPath(path, paint);
    }

    private async void OnPencilDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        if (PageContainer.Width <= 0 || PageContainer.Height <= 0 || !int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        var linePoints = e.LastDrawingLine.Points;
        if (linePoints.Count < 2)
        {
            return;
        }

        var normalizedPoints = linePoints
            .Select(p => new StrokePoint(p.X / PageContainer.Width, p.Y / PageContainer.Height))
            .ToList();

        await _viewModel.AddStrokeAsync(sheetId, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, normalizedPoints);
        AnnotationCanvas.InvalidateSurface();
        ToolPanel.IsVisible = false;
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
            ToolPanel.IsVisible = false;
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
        DrawGlyphFitted(canvas, icon.Codepoint, new SKRect(0, 0, info.Width, info.Height), icon.VisualScale);
    }

    private void OnAnnotationCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;

        foreach (var annotation in _viewModel.CurrentPageAnnotations)
        {
            if (annotation.IsStroke)
            {
                DrawStroke(canvas, annotation, info);
                continue;
            }

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

            DrawGlyphFitted(canvas, icon.Codepoint, targetRect, icon.VisualScale);
        }
    }

    private void DrawStroke(SKCanvas canvas, Annotation annotation, SKImageInfo info)
    {
        var points = AnnotationService.DeserializePoints(annotation.Points);
        if (points.Count < 2 || annotation.ColorHex is null)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)(points[0].X * info.Width), (float)(points[0].Y * info.Height));
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo((float)(points[i].X * info.Width), (float)(points[i].Y * info.Height));
        }

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(annotation.ColorHex),
            StrokeWidth = (float)(annotation.StrokeWidth * info.Width),
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };

        canvas.DrawPath(path, paint);
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
    //
    // A dragged icon's move/resize repaints this every frame (AnnotationCanvas
    // is invalidated on every PanUpdated "Running" event), so this used to
    // allocate two SKFont objects and run MeasureText twice per icon per
    // frame - on the emulator's software rendering that was slow enough to
    // visibly drop frames mid-drag (reported as "shaky" move/resize).
    // measuredBounds only depends on the typeface+codepoint, never on
    // targetRect, so it's cached once per codepoint instead of remeasured
    // every repaint; _glyphPaint is similarly a single reused instance
    // instead of a fresh allocation per glyph per frame.
    private void DrawGlyphFitted(SKCanvas canvas, int codepoint, SKRect targetRect, double visualScale = 1.0)
    {
        if (_bravuraTypeface is null || targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return;
        }

        var text = char.ConvertFromUtf32(codepoint);

        if (!_glyphBoundsCache.TryGetValue(codepoint, out var measuredBounds))
        {
            using var measureFont = new SKFont(_bravuraTypeface, 100);
            measureFont.MeasureText(text, out measuredBounds);
            _glyphBoundsCache[codepoint] = measuredBounds;
        }

        if (measuredBounds.Width <= 0 || measuredBounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(targetRect.Width / measuredBounds.Width, targetRect.Height / measuredBounds.Height) * 0.9f * (float)visualScale;
        using var font = new SKFont(_bravuraTypeface, 100f * scale);
        font.MeasureText(text, out var fittedBounds);

        var x = targetRect.MidX - fittedBounds.MidX;
        var y = targetRect.MidY - fittedBounds.MidY;

        canvas.DrawText(text, x, y, font, _glyphPaint);
    }
}
