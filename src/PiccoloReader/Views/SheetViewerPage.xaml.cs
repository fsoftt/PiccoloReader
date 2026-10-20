using CommunityToolkit.Maui.Core;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.PageTurn;
using PiccoloReader.Core.Services.Stylus;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

[QueryProperty(nameof(SheetId), "sheetId")]
public partial class SheetViewerPage : ContentPage
{
    private const double ZoomedInThreshold = 1.05;

    // Extra slack (fraction of the page) around a stroke that still counts
    // as a tap on it in the continuous list.
    private const double StrokeTapTolerance = 0.02;
    private const double PageTurnDragThreshold = 60;
    private const double SelectionHandlePadding = 20;
    private const double TrashHitTestRadius = 60;
    private const double SideTapZoneFraction = 0.3;

    private readonly SheetViewerViewModel _viewModel;
    private readonly MetronomeViewModel _metronome;

    private double _currentScale = 1;
    private double _xOffset;
    private double _yOffset;
    private double _panTotalX;
    private double _panTotalY;

    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartX;
    private double _moveStartX;
    private double _moveStartY;

    private bool _isToolbarVisible = true;
    private bool _isToolFabExpanded;

    private const double MaxContinuousZoom = 5;
    private const double MaxPagedZoom = 5;
    private double _continuousZoom = 1;
    private double _lastPageWidth;
    private double _lastPageHeight;
    private double _continuousScrollOffsetFallback;
#if ANDROID
    private AndroidX.RecyclerView.Widget.RecyclerView? _continuousRecyclerView;
    private PiccoloReader.Platforms.Android.ContinuousZoomTouchListener? _continuousZoomTouchListener;
    private double _continuousScrollRemainderPx;
#endif

    private readonly AnnotationPainter _annotationPainter = new();

    private readonly PiccoloReader.Services.SheetShareFlow _shareFlow;
    private readonly LibraryService _libraryService;
    private readonly IPageTurnInput _pageTurnInput;
    private readonly TunerStripController _tunerStrip;
    private readonly ITunerStripPreferenceService _tunerStripPreferences;
    private readonly IStylusPreferenceService _stylusPreferences;
    // Cached from the preference in OnAppearing so toolbar show/hide does not read it.
    private TunerStripPosition _tunerStripPosition;
    private Window? _window;

    public SheetViewerPage(SheetViewerViewModel viewModel, MetronomeViewModel metronome, PiccoloReader.Services.SheetShareFlow shareFlow, LibraryService libraryService, IPageTurnInput pageTurnInput, TunerStripController tunerStrip, ITunerStripPreferenceService tunerStripPreferences, IStylusPreferenceService stylusPreferences)
    {
        InitializeComponent();
        Unloaded += (_, _) => _pageTurnInput.Requested -= OnPageTurnRequested;
        _viewModel = viewModel;
        _metronome = metronome;
        _shareFlow = shareFlow;
        _libraryService = libraryService;
        _pageTurnInput = pageTurnInput;
        _tunerStrip = tunerStrip;
        _tunerStripPreferences = tunerStripPreferences;
        _stylusPreferences = stylusPreferences;
        SetUpTunerStripView();
        BindingContext = _viewModel;

        // The opening thumbnail under the page image goes once the full page
        // has decoded; removing it earlier would blank the screen.
        PageImage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == Image.IsLoadingProperty.PropertyName
                && !PageImage.IsLoading
                && _viewModel.OpeningThumbnailBytes is not null
                && !_viewModel.IsShowingThumbnail
                && !ReferenceEquals(_viewModel.CurrentPageImageBytes, _viewModel.OpeningThumbnailBytes))
            {
                _viewModel.OpeningThumbnailBytes = null;
            }
        };

        _viewModel.CurrentPageAnnotations.CollectionChanged += (_, _) => InvalidateAnnotations();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SheetViewerViewModel.ReadingMode))
            {
                HideDetail();
                UpdateReadingModeUi();
            }
            else if (e.PropertyName == nameof(SheetViewerViewModel.CurrentPageCrop))
            {
                InvalidateAnnotations();
                RepositionSelectionOverlay();
                RestartPagedDetail();
            }
            else if (e.PropertyName == nameof(SheetViewerViewModel.CurrentPageImageBytes))
            {
                if (_viewModel.CurrentPageImageBytes is not null)
                {
                    OnPageImageShown();
                }

                RestartPagedDetail();
                // The pencil's on-screen width follows the page's size.
                UpdatePencilDrawingViewLineWidth();
            }
            else if (e.PropertyName == nameof(SheetViewerViewModel.CurrentPageIndex))
            {
                HideDetail();
                UpdateBookmarkButton();
                UpdateEditingUi();
                if (!IsEditing)
                {
                    _inEditSession = false;
                }
            }
            else if (e.PropertyName is nameof(SheetViewerViewModel.CanUndo)
                or nameof(SheetViewerViewModel.CanRedo)
                or nameof(SheetViewerViewModel.SelectedAnnotation)
                or nameof(SheetViewerViewModel.IsContinuousEditing)
                or nameof(SheetViewerViewModel.ActiveTool))
            {
                UpdateEditingUi();
            }
        };
        ToolSheet.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualElement.IsVisible))
            {
                if (!ToolSheet.IsVisible)
                {
                    _toolSheetExpanded = false;
                }

                UpdateEditingUi();
                // Defensive: repaint the annotation layer whenever a tool sheet
                // opens or closes (Bug 16: a glyph stayed unpainted until the next
                // insert forced a repaint).
                InvalidateAnnotations();
            }
        };
        BuildSymbolCategoryTabs();
        UpdateToolValueLabels();
        UpdateToolSegments();
        _viewModel.Bookmarks.CollectionChanged += (_, _) => UpdateBookmarkButton();
        UpdateReadingModeIcon();
        UpdateReadingModeName();
        UpdateBookmarkButton();

        ContinuousPagesView.ItemTemplate = new DataTemplate(() =>
        {
            var view = new ContinuousPageView(_annotationPainter, OnContinuousPageTapped, () => _continuousZoom);
            _continuousViews.Add(new WeakReference<ContinuousPageView>(view));
            return view;
        });
        PageContainer.SizeChanged += OnPageContainerSizeChanged;
        ContinuousPagesView.SizeChanged += (_, _) =>
        {
            RelayoutContinuousPages();
        };

#if ANDROID
        AttachAndroidPageContainerTouchListener();
        AttachAndroidPencilPinchListener();
        AttachAndroidContinuousZoomListener();
        AttachAndroidSelectionDragListeners();
#endif
    }

    public string SheetId { get; set; } = string.Empty;

    public IReadOnlyList<MusicIconCategory> IconCategories => MusicIconCatalog.Categories;

    // The portrait reading area legacy annotations were normalized to: the
    // PageContainer of the pre-#81 viewer, i.e. the screen in portrait minus
    // the status bar, the system navigation bar and the (visible) app bar, as
    // that is when annotations were authored. Derived from the display only,
    // in dp, so it is the same for every page, orientation and toolbar state.
    internal static (double Width, double Height) LegacyPortraitReadingArea()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        if (info.Density <= 0)
        {
            return (0, 0);
        }

        var widthDp = Math.Min(info.Width, info.Height) / info.Density;
        var heightDp = Math.Max(info.Width, info.Height) / info.Density;
        var chromeDp = 56.0;

#if ANDROID
        var resources = Microsoft.Maui.ApplicationModel.Platform.AppContext.Resources;
        if (resources is not null)
        {
            chromeDp += AndroidDimenDp(resources, "status_bar_height") + AndroidDimenDp(resources, "navigation_bar_height");
        }
#endif

        return (widthDp, heightDp - chromeDp);
    }

#if ANDROID
    private static double AndroidDimenDp(Android.Content.Res.Resources resources, string name)
    {
        var id = resources.GetIdentifier(name, "dimen", "android");
        return id > 0 ? resources.GetDimensionPixelSize(id) / resources.DisplayMetrics!.Density : 0;
    }
#endif

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.SaveCurrentPageThumbnail();
        _viewModel.StopSpinnerWatchdog();
        _metronome.PropertyChanged -= OnMetronomePropertyChanged;
        _pageTurnInput.Requested -= OnPageTurnRequested;
        AttachWindow(null);
        _tunerStrip.StateChanged -= OnTunerStripStateChanged;

        _tunerStrip.Detach();
        UpdateTunerStripButton();
    }

    private void OnMetronomePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MetronomeViewModel.IsPlaying))
        {
            UpdateMetronomeButton();
        }
    }

    private void OnMetronomeClicked(object? sender, EventArgs e) =>
        _metronome.TogglePlayCommand.Execute(null);

    // The microphone is only open while the viewer is visible and the strip is on:
    // stop when hidden or the app goes to the background, restart on resume.
    private void OnWindowStopped(object? sender, EventArgs e)
    {
        _tunerStrip.Detach();
        UpdateTunerStripButton();
    }

    private void OnWindowResumed(object? sender, EventArgs e) => AttachTunerStrip();

    private void AttachWindow(Window? window)
    {
        if (ReferenceEquals(_window, window))
        {
            return;
        }

        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window.Resumed -= OnWindowResumed;
        }

        _window = window;
        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
            _window.Resumed += OnWindowResumed;
        }
    }

    private async void AttachTunerStrip()
    {
        try
        {
            // IsOn is set before the microphone opens, so show the strip right away.
            var attach = _tunerStrip.AttachAsync();
            UpdateTunerStripButton();
            await attach;
        }
        catch (Exception)
        {
            // A failed start leaves the strip off; nothing else to do.
        }

        UpdateTunerStripButton();
    }

    private async void OnTunerStripClicked(object? sender, EventArgs e)
    {
        try
        {
            await _tunerStrip.ToggleAsync();
        }
        catch (Exception)
        {
            // A failed start leaves the strip off; nothing else to do.
        }

        UpdateTunerStripButton();
    }

    // Same look as the metronome button: primary-tinted while the strip is on. The
    // two sources are built once; the strip's row stays laid out so toggling it
    // never resizes the page.
    private FontImageSource? _tunerIconOn;
    private FontImageSource? _tunerIconOff;

    private void UpdateTunerStripButton()
    {
        TunerStripButton.Source = _tunerStrip.IsOn
            ? _tunerIconOn ??= CreateTunerIcon("PrimaryLight", "PrimaryDark")
            : _tunerIconOff ??= CreateTunerIcon("TextPrimaryLight", "TextPrimaryDark");
    }

    private static FontImageSource CreateTunerIcon(string lightKey, string darkKey)
    {
        var source = new FontImageSource
        {
            Glyph = "",
            FontFamily = "MaterialOutlined",
            Size = 24
        };
        source.SetAppThemeColor(FontImageSource.ColorProperty,
            (Color)Application.Current!.Resources[lightKey],
            (Color)Application.Current!.Resources[darkKey]);
        return source;
    }

    private void OnTunerStripStateChanged(object? sender, EventArgs e) => UpdateTunerStripButton();

    // Android: the strip is a native view inside TunerStripHost, driven by the controller.
    private void SetUpTunerStripView()
    {
#if ANDROID
        var strip = new TunerStripView { HalfTrackChanged = _tunerStrip.SetHalfTrack };
        TunerStripHost.Children.Add(strip);
        _tunerStrip.Bind(strip);
#endif
        ApplyTunerStripPosition();
    }

    // Top strip: under the floating bar while it shows, at the top edge of the page area
    // while it is hidden. The margin snaps (the bar only fades).
    private Thickness TopStripMargin() => new(0, _isToolbarVisible ? TrashTopMargin : 0, 0, 0);

    private void UpdateTopTunerStripMargin()
    {
        if (_tunerStripPosition == TunerStripPosition.Top)
        {
            TunerStripHost.Margin = TopStripMargin();
        }
    }

    // Left/Right: a 6 dp column at the page edge. Bottom: the row below the page.
    // Top: overlaid on the page just under the floating bar (12 margin + 56 height + 12 gap),
    // ignoring touches. The slot is always reserved so toggling the strip never resizes the page.
    private void ApplyTunerStripPosition()
    {
        const double stripDp = 6;
        var position = _tunerStripPosition;
        var vertical = position is TunerStripPosition.Left or TunerStripPosition.Right;

        TunerStripHost.ClearValue(WidthRequestProperty);
        TunerStripHost.ClearValue(HeightRequestProperty);
        TunerStripHost.Margin = default;
        TunerStripHost.VerticalOptions = LayoutOptions.Fill;
        TunerStripHost.InputTransparent = false;
        switch (position)
        {
            case TunerStripPosition.Left:
                Grid.SetRow(TunerStripHost, 0);
                Grid.SetColumn(TunerStripHost, 0);
                Grid.SetColumnSpan(TunerStripHost, 1);
                TunerStripHost.WidthRequest = stripDp;
                break;
            case TunerStripPosition.Bottom:
                Grid.SetRow(TunerStripHost, 1);
                Grid.SetColumn(TunerStripHost, 0);
                Grid.SetColumnSpan(TunerStripHost, 3);
                TunerStripHost.HeightRequest = stripDp;
                break;
            case TunerStripPosition.Top:
                Grid.SetRow(TunerStripHost, 0);
                Grid.SetColumn(TunerStripHost, 1);
                Grid.SetColumnSpan(TunerStripHost, 1);
                TunerStripHost.VerticalOptions = LayoutOptions.Start;
                TunerStripHost.Margin = TopStripMargin();
                TunerStripHost.HeightRequest = stripDp;
                TunerStripHost.InputTransparent = true;
                break;
            default:
                Grid.SetRow(TunerStripHost, 0);
                Grid.SetColumn(TunerStripHost, 2);
                Grid.SetColumnSpan(TunerStripHost, 1);
                TunerStripHost.WidthRequest = stripDp;
                break;
        }

#if ANDROID
        if (TunerStripHost.Children.FirstOrDefault() is TunerStripView strip)
        {
            strip.WidthRequest = vertical ? stripDp : -1;
            strip.HeightRequest = vertical ? -1 : stripDp;
            strip.IsVertical = vertical;
        }
#endif
    }

    // Timer icon, primary-tinted with a crossed timer while the metronome plays.
    private void UpdateMetronomeButton()
    {
        var playing = _metronome.IsPlaying;
        var source = new FontImageSource
        {
            Glyph = playing ? "" : "",
            FontFamily = "MaterialOutlined",
            Size = 24
        };
        source.SetAppThemeColor(FontImageSource.ColorProperty,
            (Color)Application.Current!.Resources[playing ? "PrimaryLight" : "TextPrimaryLight"],
            (Color)Application.Current!.Resources[playing ? "PrimaryDark" : "TextPrimaryDark"]);
        MetronomeButton.Source = source;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _tunerStripPosition = _tunerStripPreferences.Position;
        _viewModel.StartSpinnerWatchdog();
        _metronome.PropertyChanged += OnMetronomePropertyChanged;
        _pageTurnInput.Requested -= OnPageTurnRequested;
        _pageTurnInput.Requested += OnPageTurnRequested;
        UpdateMetronomeButton();
        // Window can still be null here on a freshly pushed page; this is a single-window app.
        AttachWindow(Window ?? Application.Current?.Windows.FirstOrDefault());
        _tunerStrip.StateChanged -= OnTunerStripStateChanged;
        _tunerStrip.StateChanged += OnTunerStripStateChanged;

        ApplyTunerStripPosition();
        UpdateTunerStripButton();
        // The tuner strip starts after the first page is shown (below): opening the microphone competes with the page load.


        // The view model is reused across openings: start outside any editing
        // state so the reading bar (not the editing bar) is what shows.
        ToolSheet.IsVisible = false;
        _viewModel.ActiveTool = AnnotationTool.MusicIcons;
        _viewModel.SelectedAnnotation = null;
        SetToolbarVisible(false, animate: false);

        // The view model is reused across openings: leaving the editor with
        // an icon selected would otherwise bring the selection box and trash
        // button back on the next document. Always start with no selection.
        _viewModel.SelectedAnnotation = null;
        UpdateSelectionOverlay();

        if (!int.TryParse(SheetId, out var sheetId))
        {
            AttachTunerStrip();
            return;
        }

        // The typeface loads while the page renders; only the annotation overlay needs it.
        var bravuraTask = EnsureBravuraTypefaceLoadedAsync();
        UpdateToolSections();

        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
        _viewModel.LegacyReferenceSize = LegacyPortraitReadingArea();
        var targetWidthPx = (int)displayInfo.Width;
        var targetHeightPx = (int)displayInfo.Height;

        await _viewModel.LoadAsync(sheetId, targetWidthPx, targetHeightPx);
        await bravuraTask;
        ResetZoom();
        InvalidateAnnotations();
        UpdateReadingModeUi();
        AttachTunerStrip();
    }

    // The editor's zoom/translation (set by ApplyEditorZoomFromContinuous)
    // is in the old orientation's coordinates, so after a rotation it leaves
    // the page shifted and clipped. Resetting re-centers the page. Only an
    // orientation flip counts: the toolbar showing/hiding also resizes the page.
    private readonly List<WeakReference<ContinuousPageView>> _continuousViews = new();
    private bool _orientationFlipped;
    private double _lastListWidth;

    protected override void OnSizeAllocated(double width, double height)
    {
        var changed = width > 0 && height > 0 && (width > height) != (_lastPageWidth > _lastPageHeight);
        var hadSize = _lastPageWidth > 0;
        _lastPageWidth = width;
        _lastPageHeight = height;

        base.OnSizeAllocated(width, height);
        ApplyToolSheetHeight();

        // A rotation lands here: re-center the page and re-place the
        // selection overlay for the new size.
        if (changed && hadSize)
        {
            _orientationFlipped = true;
        }
    }

    // PageContainer's final size after a rotation arrives here once its
    // layout has settled (OnSizeAllocated runs before the children are
    // arranged), so zoom, selection overlay and the list's annotation
    // frames are re-derived now rather than after a fixed delay.
    private void OnPageContainerSizeChanged(object? sender, EventArgs e)
    {
        if (PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        UpdatePencilDrawingViewLineWidth();

        if (_orientationFlipped)
        {
            ResetZoom();
        }
        else
        {
            RefreshZoomAfterResize();
        }

        _orientationFlipped = false;
        RestartPagedDetail();
        InvalidateContinuousAnnotations();
    }

    private void RelayoutContinuousPages()
    {
        var width = ContinuousPagesView.Width;
        if (width <= 0 || Math.Abs(width - _lastListWidth) < 0.5)
        {
            return;
        }

        _lastListWidth = width;
        ScheduleDetail();

        _continuousViews.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var reference in _continuousViews)
        {
            if (reference.TryGetTarget(out var view))
            {
                view.Relayout(width);
            }
        }

#if ANDROID
        // RecyclerView keeps the rows measured at the old width.
        _continuousRecyclerView?.GetAdapter()?.NotifyDataSetChanged();
#endif

        // The rows now on screen may differ after the relayout without any
        // scroll event firing, so their pages would never be requested and
        // their loading spinners would keep running.
        Dispatcher.Dispatch(async () => await UpdateContinuousViewportAsync());
    }

    private void InvalidateContinuousAnnotations()
    {
        _continuousViews.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var reference in _continuousViews)
        {
            if (reference.TryGetTarget(out var view))
            {
                view.InvalidateAnnotations();
            }
        }
    }

    // The page's rectangle within PageContainer, in PageContainer-local
    // units. Annotations are normalized to this rectangle, not to the whole
    // container (the page is letterboxed inside it). Falls back to the whole
    // container while the page's aspect ratio is unknown.
    private PageFrame EditorPageFrame() =>
        PageFrame.Fit(PageContainer.Width, PageContainer.Height, _viewModel.CurrentPageAspectRatio, _viewModel.CurrentPageCrop);

    private Rect EditorPageRect()
    {
        var frame = EditorPageFrame();
        return new Rect(
            frame.X * PageContainer.Width,
            frame.Y * PageContainer.Height,
            frame.Width * PageContainer.Width,
            frame.Height * PageContainer.Height);
    }

    // The part of the page actually on screen (the crop) in PageContainer-
    // local units. Equals EditorPageRect() when the page is not cropped.
    private Rect EditorVisibleRect()
    {
        var frame = EditorPageFrame().VisibleRegion(_viewModel.CurrentPageCrop);
        return new Rect(
            frame.X * PageContainer.Width,
            frame.Y * PageContainer.Height,
            frame.Width * PageContainer.Width,
            frame.Height * PageContainer.Height);
    }

    // The part of the page on screen right now, in full-page coordinates:
    // the reading area (PageContainer's untransformed bounds) mapped back
    // through the zoom and pan. Null while not zoomed in.
    private PageCrop? EditorVisibleArea()
    {
        if (_currentScale <= ZoomedInThreshold || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return null;
        }

        var pageRect = EditorPageRect();
        var left = -PageContainer.TranslationX / _currentScale;
        var top = -PageContainer.TranslationY / _currentScale;
        return PageViewport.VisiblePart(
            pageRect.Left, pageRect.Top, pageRect.Width, pageRect.Height,
            left, top, left + PageContainer.Width / _currentScale, top + PageContainer.Height / _currentScale,
            _viewModel.CurrentPageCrop);
    }

    // Sharp zoomed-in view ("detail"). The page image is rendered once at
    // screen width, so zooming alone only magnifies those pixels. Once a
    // pinch/pan/scroll has settled, the part of the page that is on screen is
    // re-rendered at its on-screen resolution and laid over the base image.
    // The overlay is dropped the moment anything moves, so the (blurrier) base
    // image underneath is what shows during gestures and the swap is never a
    // flash of nothing.
    private const int DetailDebounceMs = 150;
    private CancellationTokenSource? _detailCts;
    private bool _continuousDetailShown;
    private RectD _annotationDetailArea;
    private bool _annotationDetailPainted;

    // A page/crop/size change in the paged view or the editor: whatever was
    // rendered no longer matches, so redo it if still zoomed in.
    private void RestartPagedDetail()
    {
        if (!IsContinuousListActive)
        {
            ScheduleDetail();
        }
    }

    private bool IsContinuousListActive => _viewModel.IsContinuousReading && !_viewModel.IsContinuousEditing;

    private bool IsZoomedForDetail => IsContinuousListActive ? _continuousZoom > ZoomedInThreshold : _currentScale > ZoomedInThreshold;

    // Cancels a pending/in-flight detail render and removes the overlay.
    private void HideDetail()
    {
        _detailCts?.Cancel();
        _detailCts = null;

        if (DetailImage.IsVisible)
        {
            DetailImage.IsVisible = false;
            DetailImage.Source = null;
        }

        if (AnnotationDetailCanvas.IsVisible)
        {
            // The base canvas draws the annotations again.
            AnnotationDetailCanvas.IsVisible = false;
            _annotationDetailPainted = false;
            AnnotationCanvas.InvalidateSurface();
        }

        if (_continuousDetailShown)
        {
            _continuousDetailShown = false;
            foreach (var reference in _continuousViews)
            {
                if (reference.TryGetTarget(out var view))
                {
                    view.HideDetail();
                }
            }
        }
    }

    // Hides the overlay and, if still zoomed in, requests a fresh one once
    // things have been quiet for DetailDebounceMs.
    private void ScheduleDetail()
    {
        HideDetail();
        if (!IsZoomedForDetail)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _detailCts = cts;
        _ = RunDetailAsync(cts.Token);
    }

    private async Task RunDetailAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DetailDebounceMs, ct);
            if (IsContinuousListActive)
            {
                await ShowContinuousDetailAsync(ct);
            }
            else
            {
                await ShowPagedDetailAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Best effort: the base image is still there.
        }
    }

    private static int DetailMaxSidePx()
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        return (int)Math.Max(display.Width, display.Height);
    }

    private async Task ShowPagedDetailAsync(CancellationToken ct)
    {
        var width = PageContainer.Width;
        var height = PageContainer.Height;
        var scale = PageContainer.Scale;
        if (_currentScale <= ZoomedInThreshold || width <= 0 || height <= 0 || _viewModel.CurrentPageAspectRatio <= 0)
        {
            return;
        }

        var pageIndex = _viewModel.CurrentPageIndex;
        var visible = DetailRegion.VisibleLocalRect(width, height, scale, PageContainer.TranslationX, PageContainer.TranslationY);
        var imageRect = EditorVisibleRect();
        var image = new RectD(imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height);
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        if (DetailRegion.Compute(visible, image, scale, density, DetailMaxSidePx()) is not { } region)
        {
            return;
        }

        var bytes = await _viewModel.RenderPageDetailAsync(pageIndex, region, ct);
        ct.ThrowIfCancellationRequested();
        if (bytes is null || pageIndex != _viewModel.CurrentPageIndex)
        {
            return;
        }

        DetailImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        DetailImage.WidthRequest = DetailRegion.LayoutRequest(region.Local.Width, density);
        DetailImage.HeightRequest = DetailRegion.LayoutRequest(region.Local.Height, density);
        DetailImage.TranslationX = region.Local.X;
        DetailImage.TranslationY = region.Local.Y;
        DetailImage.IsVisible = true;

        // Annotations over the same part, drawn at screen resolution: sized
        // `scale` times larger and scaled back down, so it covers the region
        // without magnifying its pixels.
        _annotationDetailArea = region.Local;
        AnnotationDetailCanvas.WidthRequest = DetailRegion.LayoutRequest(region.Local.Width * scale, density);
        AnnotationDetailCanvas.HeightRequest = DetailRegion.LayoutRequest(region.Local.Height * scale, density);
        AnnotationDetailCanvas.Scale = 1 / scale;
        AnnotationDetailCanvas.TranslationX = region.Local.X;
        AnnotationDetailCanvas.TranslationY = region.Local.Y;
        _annotationDetailPainted = false;
        AnnotationDetailCanvas.IsVisible = true;
        AnnotationDetailCanvas.InvalidateSurface();
    }

    // Repaints the annotation layer: the sharp overlay while it is shown,
    // otherwise (also) the base canvas.
    private void InvalidateAnnotations()
    {
        AnnotationCanvas.InvalidateSurface();
        if (AnnotationDetailCanvas.IsVisible)
        {
            AnnotationDetailCanvas.InvalidateSurface();
        }
    }

    // The list is zoomed as a whole, so the visible part of its content
    // (x from the horizontal shift, y from the scroll offset) can span a few
    // pages; each page that intersects it gets its own overlay.
    private async Task ShowContinuousDetailAsync(CancellationToken ct)
    {
        var width = ContinuousPagesView.Width;
        var height = ContinuousPagesView.Height;
        var zoom = _continuousZoom;
        if (zoom <= ZoomedInThreshold || width <= 0 || height <= 0)
        {
            return;
        }

        var pages = _viewModel.ContinuousPages;
        var scroll = ContinuousScrollOffset();
        var visible = new RectD(-ContinuousPagesView.TranslationX / zoom, scroll, width / zoom, height / zoom);
        // Rows are laid out in whole pixels (each rounded up), so after a few
        // pages they sit a pixel or more off the sum of their heights in
        // units. Where the platform can tell, use each row's real position:
        // positions are then relative to the viewport (scroll 0).
        var viewport = new RectD(visible.X, 0, visible.Width, visible.Height);
        var first = ContinuousLayout.PageAt(pages, visible.Y, width, ContinuousPageView.Spacing);
        var last = ContinuousLayout.PageAt(pages, visible.Bottom, width, ContinuousPageView.Spacing);
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;

        for (var index = first; index <= last && index < pages.Count; index++)
        {
            var page = pages[index];
            if (page.ImageBytes is null)
            {
                continue;
            }

            var top = ContinuousLayout.PageTop(pages, index, width, ContinuousPageView.Spacing);
            var pageHeight = ContinuousLayout.PageHeight(page, width);
            var area = visible;
            if (TryGetContinuousRow(index, density, out var rowTop, out var rowHeight))
            {
                top = rowTop;
                pageHeight = rowHeight - ContinuousPageView.Spacing;
                area = viewport;
            }

            // The row height is rounded to whole units; the image is AspectFit inside it.
            var fit = DetailRegion.AspectFit(width, pageHeight, page.AspectRatio);
            var image = new RectD(fit.X, top + fit.Y, fit.Width, fit.Height);
            if (DetailRegion.Compute(area, image, zoom, density, DetailMaxSidePx()) is not { } region)
            {
                continue;
            }

            var bytes = await _viewModel.RenderPageDetailAsync(index, region, ct);
            ct.ThrowIfCancellationRequested();
            if (bytes is null)
            {
                continue;
            }

            _continuousViews.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var reference in _continuousViews)
            {
                if (reference.TryGetTarget(out var view) && ReferenceEquals(view.Page, page))
                {
                    view.ShowDetail(bytes, new RectD(region.Local.X, region.Local.Y - top, region.Local.Width, region.Local.Height), density, zoom);
                    _continuousDetailShown = true;
                    break;
                }
            }
        }
    }

    // The position (relative to the list's viewport) and height of a page's
    // row as the platform actually laid it out, in units. False when the
    // row isn't laid out (or off Android), where the computed layout stands.
    private bool TryGetContinuousRow(int index, double density, out double top, out double height)
    {
        top = 0;
        height = 0;
#if ANDROID
        if (_continuousRecyclerView?.GetLayoutManager()?.FindViewByPosition(index) is { } child && child.Height > 0)
        {
            top = child.Top / density;
            height = child.Height / density;
            return true;
        }
#endif
        return false;
    }

    // Landscape pages are shown fit to width (see PagedViewport.BaseScale):
    // the zoom a "reset" returns to, 1 otherwise. _currentScale is always the
    // absolute scale of PageContainer, so annotation mapping and detail
    // rendering (which compare against 1) need no knowledge of it.
    private double _baseScale = 1;
    private int _pageShownIndex = -1;
    // A page turn waiting for the new page's image (its aspect ratio/crop are
    // unknown until it renders): where to put it once it shows.
    private bool? _pendingPageAlignBottom;
    private int _pendingPageIndex = -1;
    // Container size the current scale/translation were computed for.
    private double _zoomGeometryWidth;
    private double _zoomGeometryHeight;

    private bool FitToPageExtent => _baseScale > 1.0001;

    private bool HasPendingPageTurn =>
        _pendingPageAlignBottom is not null && _pendingPageIndex == _viewModel.CurrentPageIndex;

    // The zoom a page turn keeps (see PagedViewport.CarryRatio): the ratio to
    // the page base scale and where in the horizontal pan range the view was.
    private readonly record struct ZoomCarry(double Ratio, double XFraction);

    private ZoomCarry? _pendingCarry;

    private ZoomCarry? CaptureZoomCarry()
    {
        if (PageContainer.Width <= 0 || PageContainer.Height <= 0
            || PagedViewport.CarryRatio(_currentScale, _baseScale, ZoomedInThreshold) is not { } ratio)
        {
            return null;
        }

        var visible = EditorVisibleRect();
        var (min, max) = PagedViewport.Range(
            PageContainer.Width, _currentScale, visible.Left, visible.Right, FitToPageExtent);
        return new ZoomCarry(ratio, PagedViewport.PanFraction(PageContainer.TranslationX, min, max));
    }

    // Reset to the base zoom of the current page (opening, rotation, mode
    // switch, crop...): nothing is carried.
    private void ResetZoom()
    {
        _pendingCarry = null;
        if (HasPendingPageTurn)
        {
            // E.g. a rotation while the next page loads: its base zoom is
            // applied (with the new size) when the image arrives.
            HideDetail();
            return;
        }

        _pendingPageAlignBottom = null;
        ApplyBaseZoom(alignBottom: false);
    }

    // After a page turn the new page aspect ratio/crop may not be known yet
    // (it renders asynchronously); when so, the base zoom is applied once its
    // image arrives (OnPageImageShown).
    private void ResetZoomForNewPage(bool alignBottom, ZoomCarry? carry)
    {
        if (_viewModel.CurrentPageIndex != _pageShownIndex)
        {
            _pendingPageAlignBottom = alignBottom;
            _pendingPageIndex = _viewModel.CurrentPageIndex;
            _pendingCarry = carry;
            HideDetail();
            return;
        }

        _pendingPageAlignBottom = null;
        _pendingCarry = null;
        ApplyBaseZoom(alignBottom, carry);
    }

    private void OnPageImageShown()
    {
        _pageShownIndex = _viewModel.CurrentPageIndex;
        var pending = HasPendingPageTurn ? _pendingPageAlignBottom : null;
        var carry = HasPendingPageTurn ? _pendingCarry : null;
        // A pending turn for another page was superseded.
        _pendingPageAlignBottom = null;
        _pendingCarry = null;
        if (pending is { } alignBottom)
        {
            ApplyBaseZoom(alignBottom, carry);
        }
    }

    private Rect VisibleRectFor(double width, double height)
    {
        var frame = PageFrame.Fit(width, height, _viewModel.CurrentPageAspectRatio, _viewModel.CurrentPageCrop)
            .VisibleRegion(_viewModel.CurrentPageCrop);
        return new Rect(frame.X * width, frame.Y * height, frame.Width * width, frame.Height * height);
    }

    private void ApplyBaseZoom(bool alignBottom, ZoomCarry? carry = null)
    {
        HideDetail();
        var width = PageContainer.Width;
        var height = PageContainer.Height;
        _baseScale = width > 0 && height > 0
            ? PagedViewport.BaseScale(width, height, _viewModel.CurrentPageAspectRatio, _viewModel.CurrentPageCrop, MaxPagedZoom)
            : 1;
        // A page turn keeps the zoom relative to the new page's base scale.
        _currentScale = carry is { } kept && width > 0 && height > 0
            ? PagedViewport.CarryScale(_baseScale, kept.Ratio, MaxPagedZoom)
            : _baseScale;
        PageContainer.AnchorX = 0;
        PageContainer.AnchorY = 0;
        PageContainer.Scale = _currentScale;
        if (_currentScale > 1.0001)
        {
            var visible = EditorVisibleRect();
            var fit = FitToPageExtent;
            PageContainer.TranslationY = PagedViewport.AlignY(height, _currentScale, visible.Top, visible.Bottom, alignBottom, fit);
            if (carry is { } c)
            {
                var (minX, maxX) = PagedViewport.Range(width, _currentScale, visible.Left, visible.Right, fit);
                PageContainer.TranslationX = PagedViewport.TranslationFromFraction(c.XFraction, minX, maxX);
            }
            else
            {
                PageContainer.TranslationX = PagedViewport.AlignY(width, _currentScale, visible.Left, visible.Right, false, fit);
            }
        }
        else
        {
            PageContainer.TranslationX = 0;
            PageContainer.TranslationY = 0;
        }

        _xOffset = PageContainer.TranslationX;
        _yOffset = PageContainer.TranslationY;
        _zoomGeometryWidth = width;
        _zoomGeometryHeight = height;
        UpdateResizeHandleScale();
        UpdateSelectionOverlay();
        ScheduleDetail();
    }

    // The container changed size without a rotation (banner, tuner strip,
    // toolbar): follow the new fit-width scale but keep where the user is
    // reading (same part of the page at the top of the screen) instead of
    // jumping back to the page top.
    private void RefreshZoomAfterResize()
    {
        var width = PageContainer.Width;
        var height = PageContainer.Height;
        if (HasPendingPageTurn)
        {
            UpdateSelectionOverlay();
            return;
        }

        if (Math.Abs(_currentScale - _baseScale) >= 0.001 || _zoomGeometryHeight <= 0)
        {
            // Zoomed beyond the base: only keep the pan inside the new bounds.
            ClampPageTranslation();
            _xOffset = PageContainer.TranslationX;
            _yOffset = PageContainer.TranslationY;
            _zoomGeometryWidth = width;
            _zoomGeometryHeight = height;
            UpdateSelectionOverlay();
            return;
        }

        // Fraction of the page height above the top of the screen, measured
        // with the geometry the translation was computed for.
        var old = VisibleRectFor(_zoomGeometryWidth, _zoomGeometryHeight);
        var topFraction = old.Height > 0 && _baseScale > 1.0001
            ? ((-PageContainer.TranslationY / _baseScale) - old.Top) / old.Height
            : 0;

        HideDetail();
        _baseScale = PagedViewport.BaseScale(width, height, _viewModel.CurrentPageAspectRatio, _viewModel.CurrentPageCrop, MaxPagedZoom);
        _currentScale = _baseScale;
        PageContainer.Scale = _baseScale;
        if (FitToPageExtent)
        {
            var visible = EditorVisibleRect();
            PageContainer.TranslationX = PagedViewport.AlignY(width, _baseScale, visible.Left, visible.Right, false, true);
            PageContainer.TranslationY = PagedViewport.Clamp(
                -(visible.Top + topFraction * visible.Height) * _baseScale, height, _baseScale, visible.Top, visible.Bottom, true);
        }
        else
        {
            PageContainer.TranslationX = 0;
            PageContainer.TranslationY = 0;
        }

        _xOffset = PageContainer.TranslationX;
        _yOffset = PageContainer.TranslationY;
        _zoomGeometryWidth = width;
        _zoomGeometryHeight = height;
        UpdateResizeHandleScale();
        UpdateSelectionOverlay();
        ScheduleDetail();
    }

    // Keeps PageContainer's translation inside the allowed pan range.
    private void ClampPageTranslation()
    {
        var visible = EditorVisibleRect();
        PageContainer.TranslationX = PagedViewport.Clamp(
            PageContainer.TranslationX, PageContainer.Width, _currentScale, visible.Left, visible.Right, FitToPageExtent);
        PageContainer.TranslationY = PagedViewport.Clamp(
            PageContainer.TranslationY, PageContainer.Height, _currentScale, visible.Top, visible.Bottom, FitToPageExtent);
    }

    // Whether a drag along an axis can move the page (it has room to pan).
    private bool CanPanAxis(bool horizontal)
    {
        if (_currentScale <= ZoomedInThreshold)
        {
            return false;
        }

        var visible = EditorVisibleRect();
        var (min, max) = horizontal
            ? PagedViewport.Range(PageContainer.Width, _currentScale, visible.Left, visible.Right, FitToPageExtent)
            : PagedViewport.Range(PageContainer.Height, _currentScale, visible.Top, visible.Bottom, FitToPageExtent);
        return max - min > 2;
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
    // Left/right 30% zones turn pages (the original behavior); the middle
    // 40% toggles the toolbar. Matches standard reader apps: tap the edges
    // to navigate, tap the middle to reveal/hide chrome. Side taps are
    // ignored while zoomed in (ZoomedInThreshold) since the user is far
    // more likely to be panning around than trying to turn the page; the
    // center toggle isn't gated by zoom - it should always work.
    private void OnPageContainerTapped(object? sender, TappedEventArgs e)
    {
        if (ToolSheet.IsVisible)
        {
            ToolSheet.IsVisible = false;
            return;
        }

        if (_isToolFabExpanded)
        {
            SetToolFabExpanded(false);
            return;
        }

        var position = e.GetPosition(PageContainer);
        if (position is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        HandleContainerTap(position.Value.X / PageContainer.Width, position.Value.Y / PageContainer.Height);
    }

    // Shared by OnPageContainerTapped (iOS) and the Android touch
    // listener's tap path - see ApplyPinchTranslation for why the
    // platforms split here. ToolSheet-dismissal is handled by each
    // caller before this, same as the original single handler did, since
    // the Android listener needs to skip its own hit-slop/tap-vs-pan
    // classification in that case too.
    private void HandleContainerTap(double normalizedX, double normalizedY)
    {
        // Annotations are page-normalized, the tap is container-normalized.
        var pageRect = EditorPageRect();
        var pageX = (normalizedX * PageContainer.Width - pageRect.Left) / pageRect.Width;
        var pageY = (normalizedY * PageContainer.Height - pageRect.Top) / pageRect.Height;
        // Annotations outside the crop are not on screen, so not tappable.
        var hit = !_viewModel.CurrentPageCrop.Contains(pageX, pageY)
            ? null
            : _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
                pageX >= a.X && pageX <= a.X + a.Width &&
                pageY >= a.Y && pageY <= a.Y + a.Height);

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

        // In vertical reading the same 30% zones sit at the top (previous)
        // and bottom (next) edges instead of left/right. The zones are
        // measured on the visible viewport, not the (zoomed, shifted) page,
        // and turn the page at any zoom.
        var tapPosition = _viewModel.IsVerticalPaged
            ? PageContainer.TranslationY / PageContainer.Height + normalizedY * _currentScale
            : PageContainer.TranslationX / PageContainer.Width + normalizedX * _currentScale;

        if (tapPosition < SideTapZoneFraction)
        {
            TryGoToPreviousPage();
        }
        else if (tapPosition > 1 - SideTapZoneFraction)
        {
            TryGoToNextPage();
        }
        else
        {
            // No tool is active here - while Pencil/Eraser is active, the
            // relevant DrawingView covers PageContainer and captures the
            // touch stream itself (see UpdateToolSections), so this handler
            // never runs in that state. That's what satisfies "hide again
            // only when no tool is selected" - no extra check needed here.
            SetToolbarVisible(!_isToolbarVisible);
        }
    }

    // The reading chrome (floating top bar, page indicator pill and tool FAB)
    // fades in and out together. Hidden chrome is also IsVisible=false so it
    // can't steal touches meant for the page. The Shell nav bar is always
    // hidden for this page (Shell.NavBarIsVisible=False in XAML), so there is
    // no Shell app bar to collapse (#82) and the page keeps a stable layout.
    private const uint ChromeFadeMs = 150;
    private int _chromeFadeVersion;

    private void SetToolbarVisible(bool visible, bool animate = true)
    {
        // The editing bar holds "Listo": it never hides while editing.
        if (!visible && IsEditing)
        {
            visible = true;
        }

        _isToolbarVisible = visible;
        UpdateTopTunerStripMargin();

        // MainToolFab is the replacement for what used to be a toolbar
        // button, so it follows the same visibility - collapsing the
        // speed-dial menu first if it happened to be open when the chrome
        // is hidden, rather than leaving orphaned mini FABs on screen.
        if (!visible)
        {
            SetToolFabExpanded(false);
        }

        var version = ++_chromeFadeVersion;
        VisualElement[] chrome = [FloatingBar, PageIndicator, MainToolFab];

        if (!animate || !IsLoaded)
        {
            foreach (var element in chrome)
            {
                element.AbortAnimation("FadeTo");
                element.Opacity = visible ? 1 : 0;
                element.IsVisible = visible;
            }

            return;
        }

        _ = FadeChromeAsync(chrome, visible, version);
    }

    private async Task FadeChromeAsync(VisualElement[] chrome, bool visible, int version)
    {
        if (visible)
        {
            foreach (var element in chrome)
            {
                element.IsVisible = true;
            }
        }

        await Task.WhenAll(chrome.Select(element => element.FadeTo(visible ? 1 : 0, ChromeFadeMs)));

        if (!visible && version == _chromeFadeVersion)
        {
            foreach (var element in chrome)
            {
                element.IsVisible = false;
            }
        }
    }

#if ANDROID
    private static void RequestAndroidWindowInsetsRefresh()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        activity?.Window?.DecorView?.RequestApplyInsets();
    }
#endif

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
        InvalidateAnnotations();
        RepositionSelectionOverlay();

        // A fresh selection (new tap, newly placed icon) always starts
        // with both controls visible. Repositioning alone - the per-frame
        // case during an active drag - must NOT reach this: it would
        // immediately re-show the controls SetDragControlsVisible(false)
        // just hid on GestureStatus.Started, on the very next Running
        // frame. That's why the drag handlers below call
        // RepositionSelectionOverlay() directly instead of this method.
        SetDragControlsVisible(_viewModel.SelectedAnnotation is not null);
        UpdateEditingUi();
        CollapseSheetIfSelectionHidden();
    }

    private bool _collapsingToolSheet;

    // Landscape leaves little height: a selection hidden under the open tool
    // sheet is collapsed so the annotation stays visible. Only done when the
    // selection itself changes (never from the layout/visibility callbacks
    // that run when the user explicitly opens the sheet), and guarded against
    // re-entrancy: collapse -> UpdateEditingUi -> reposition used to loop.
    private void CollapseSheetIfSelectionHidden()
    {
        if (_collapsingToolSheet || !ToolSheet.IsVisible || Width <= Height
            || _viewModel.SelectedAnnotation is not { } annotation
            || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        var pageRect = EditorPageRect();
        var bottom = PageContainer.Y + PageContainer.TranslationY
            + (pageRect.Top + (annotation.Y + annotation.Height) * pageRect.Height) * _currentScale;
        if (bottom <= Height - ToolSheet.HeightRequest)
        {
            return;
        }

        _collapsingToolSheet = true;
        try
        {
            ToolSheet.IsVisible = false;
        }
        finally
        {
            _collapsingToolSheet = false;
        }
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

        var pageRect = EditorPageRect();
        var left = pageRect.Left + annotation.X * pageRect.Width;
        var top = pageRect.Top + annotation.Y * pageRect.Height;
        var width = annotation.Width * pageRect.Width;
        var height = annotation.Height * pageRect.Height;

        SelectionOverlay.WidthRequest = width + SelectionHandlePadding * 2;
        SelectionOverlay.HeightRequest = height + SelectionHandlePadding * 2;
        SelectionOverlay.TranslationX = left - SelectionHandlePadding;
        SelectionOverlay.TranslationY = top - SelectionHandlePadding;

        ResizeHandle.TranslationX = SelectionHandlePadding + width - ResizeHandle.WidthRequest / 2;
        ResizeHandle.TranslationY = SelectionHandlePadding + height - ResizeHandle.HeightRequest / 2;
        UpdateResizeHandleScale();

        if (ResizeHandle.IsVisible)
        {
            UpdateTrashPlacement(annotation);
        }
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

    // The trash button sits bottom-center, which is exactly where the resize
    // handle ends up when the selected icon is centered near the bottom of
    // the screen: the button covered the handle and it couldn't be grabbed.
    // While the handle is showing, if it would overlap the button, park the
    // button top-center instead (same size, same tap/drop behavior). It's
    // not moved mid-drag (the handle is hidden then) so the drop target
    // doesn't jump under the finger; bottom-center stays the default.
    private const double TrashMargin = 88;
    // Below the floating editing bar (12 margin + 56 height + 12 gap).
    private const double TrashTopMargin = 80;

    // With the tool sheet open the trash sits just above it instead.
    private double TrashBottomMargin => ToolSheet.IsVisible ? ToolSheet.HeightRequest + 16 : TrashMargin;

    // Picks the first trash position that touches neither the selection box
    // (plus the resize handle's reach) nor the tool sheet: bottom-center
    // (above the sheet when it is open), top-center, then the corners. The
    // sheet is accounted for by TrashBottomMargin; the top positions are
    // always clear of it. Not re-evaluated mid-drag (handle hidden) so the
    // drop target doesn't jump under the finger.
    private void UpdateTrashPlacement(Annotation annotation)
    {
        if (TrashTarget.Parent is not VisualElement host || host.Width <= 0 || host.Height <= 0)
        {
            return;
        }

        var pageRect = EditorPageRect();
        double ToHostX(double pageX) => PageContainer.X + PageContainer.TranslationX + (pageRect.Left + pageX * pageRect.Width) * _currentScale;
        double ToHostY(double pageY) => PageContainer.Y + PageContainer.TranslationY + (pageRect.Top + pageY * pageRect.Height) * _currentScale;

        var reach = ResizeHandle.WidthRequest / 2 + 12;
        var box = new Rect(
            ToHostX(annotation.X) - reach,
            ToHostY(annotation.Y) - reach,
            annotation.Width * pageRect.Width * _currentScale + reach * 2,
            annotation.Height * pageRect.Height * _currentScale + reach * 2);

        var size = TrashTarget.WidthRequest;
        var bottomY = host.Height - TrashBottomMargin - TrashTarget.HeightRequest;
        var topY = TrashTopMargin;
        const double sideMargin = 24;
        var centerX = (host.Width - size) / 2;

        // (vertical alignment, horizontal alignment, x, y)
        var candidates = new (LayoutAlignment V, LayoutAlignment H, double X, double Y)[]
        {
            (LayoutAlignment.End, LayoutAlignment.Center, centerX, bottomY),
            (LayoutAlignment.Start, LayoutAlignment.Center, centerX, topY),
            (LayoutAlignment.End, LayoutAlignment.Start, sideMargin, bottomY),
            (LayoutAlignment.End, LayoutAlignment.End, host.Width - size - sideMargin, bottomY),
            (LayoutAlignment.Start, LayoutAlignment.Start, sideMargin, topY),
            (LayoutAlignment.Start, LayoutAlignment.End, host.Width - size - sideMargin, topY),
        };

        var chosen = candidates[0];
        foreach (var c in candidates)
        {
            if (!box.IntersectsWith(new Rect(c.X, c.Y, size, TrashTarget.HeightRequest)))
            {
                chosen = c;
                break;
            }
        }

        TrashTarget.VerticalOptions = new LayoutOptions(chosen.V, false);
        TrashTarget.HorizontalOptions = new LayoutOptions(chosen.H, false);
        TrashTarget.Margin = new Thickness(
            chosen.H == LayoutAlignment.Start ? sideMargin : 0,
            chosen.V == LayoutAlignment.Start ? TrashTopMargin : 0,
            chosen.H == LayoutAlignment.End ? sideMargin : 0,
            chosen.V == LayoutAlignment.End ? TrashBottomMargin : 0);
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
        if (visible && _viewModel.SelectedAnnotation is { } selected)
        {
            UpdateTrashPlacement(selected);
        }
    }

    private void OnSelectionMovePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                HandleSelectionMoveStarted();
                break;

            case GestureStatus.Running:
                HandleSelectionMoveRunning(e.TotalX, e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _ = HandleSelectionMoveEndedAsync();
                break;
        }
    }

    private void HandleSelectionMoveStarted()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        _moveStartX = annotation.X;
        _moveStartY = annotation.Y;
        SetDragControlsVisible(false);
    }

    // totalX/totalY are raw screen-pixel deltas (DP), unaffected by
    // PageContainer's own zoom Scale - true on iOS via MAUI's
    // PanGestureRecognizer, and true on Android via
    // SingleDragTouchListener's use of MotionEvent.RawX/RawY (see that
    // class's comment - confirmed on a real device that PanUpdatedEventArgs.
    // TotalX/TotalY do NOT hold, despite the name, since SelectionBorder is
    // nested inside PageContainer's live Scale transform). At zoom S, the
    // same screen-pixel drag covers 1/S as much of the page, so the delta
    // must be scaled down by _currentScale before normalizing - dividing by
    // the page rectangle's width*_currentScale (the page's actual on-screen
    // size) instead of just its unscaled width does that in one step. At the
    // default zoom (_currentScale == 1) this is identical to just dividing
    // by the page rectangle's size.
    private void HandleSelectionMoveRunning(double totalX, double totalY)
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        var pageRect = EditorPageRect();
        annotation.X = _moveStartX + totalX / (pageRect.Width * _currentScale);
        annotation.Y = _moveStartY + totalY / (pageRect.Height * _currentScale);
        RepositionSelectionOverlay();
        InvalidateAnnotations();
    }

    private async Task HandleSelectionMoveEndedAsync()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        SetDragControlsVisible(true);
        if (IsOverTrashTarget(annotation))
        {
            // Must be awaited, not fire-and-forget, before
            // UpdateSelectionOverlay() runs - otherwise the overlay reads
            // SelectedAnnotation's still-live (pre-delete) state and
            // repositions itself at the drop point instead of hiding,
            // leaving a stale border/handle behind once the delete
            // actually lands a moment later (confirmed on-device: the
            // icon itself vanished from the page - the delete worked -
            // but its selection border stayed stuck at the drop point).
            await _viewModel.DeleteSelectedAnnotationCommand.ExecuteAsync(null);
            UpdateSelectionOverlay();
        }
        else
        {
            _ = _viewModel.MoveSelectedAnnotationAsync(_moveStartX, _moveStartY, annotation.X, annotation.Y);
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

        var pageRect = EditorPageRect();
        var localCenterX = pageRect.Left + (annotation.X + annotation.Width / 2) * pageRect.Width;
        var localCenterY = pageRect.Top + (annotation.Y + annotation.Height / 2) * pageRect.Height;

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
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                HandleSelectionResizeStarted();
                break;

            case GestureStatus.Running:
                HandleSelectionResizeRunning(e.TotalX, e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                HandleSelectionResizeEnded();
                break;
        }
    }

    private void HandleSelectionResizeStarted()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        _resizeStartWidth = annotation.Width;
        _resizeStartHeight = annotation.Height;
        _resizeStartX = annotation.X;
        SetDragControlsVisible(false);
    }

    // See the matching comment on HandleSelectionMoveRunning - totalX/
    // totalY need dividing by _currentScale before normalizing, or a
    // resize drag while zoomed in ends up several times larger than the
    // finger's own travel.
    private void HandleSelectionResizeRunning(double totalX, double totalY)
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        var pageRect = EditorPageRect();
        annotation.Width = Math.Max(0.02, _resizeStartWidth + totalX / (pageRect.Width * _currentScale));
        annotation.Height = Math.Max(0.02, _resizeStartHeight + totalY / (pageRect.Height * _currentScale));
        if (HairpinGeometry.IsHairpin(annotation.IconKey))
        {
            // Horizontal drag lengthens the hairpin, vertical drag opens it.
            (annotation.X, annotation.Width, annotation.Height) = HairpinGeometry.ClampBox(
                _resizeStartX, annotation.Width, annotation.Height,
                _viewModel.CurrentPageAspectRatio, _viewModel.CurrentPageCrop.Right);
        }

        RepositionSelectionOverlay();
        InvalidateAnnotations();
    }

    private void HandleSelectionResizeEnded()
    {
        var annotation = _viewModel.SelectedAnnotation;
        if (annotation is null)
        {
            return;
        }

        SetDragControlsVisible(true);
        _ = ResizeSelectedAnnotationSafeAsync(annotation.Width, annotation.Height);
    }

    private async Task ResizeSelectedAnnotationSafeAsync(double width, double height)
    {
        try
        {
            await _viewModel.ResizeSelectedAnnotationAsync(_resizeStartWidth, _resizeStartHeight, width, height, _resizeStartX);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Resize annotation failed: {ex}");
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
    // scaled by the view's starting scale. Multiplying by that starting
    // scale again here (as an earlier version of this code did, copying a
    // formula meant for a raw/unscaled delta) squares that factor:
    // harmless at scale=1 (1*1=1, why zoom-in "looked fine" from a fresh
    // page), but wildly overcorrects once already zoomed in - which is
    // why zooming back out was broken. Only the *delta* needs adding, no
    // extra multiplication.
    //
    // The translation (zoom-to-pinch-point) math below used to be copied
    // from Microsoft's classic two-element pinch-zoom recipe (a fixed
    // outer viewport + a separately-sized inner content view), recomputed
    // every Running frame from the *gesture-start* scale/translation
    // snapshot. PageContainer plays both
    // roles at once (it's the gesture host AND the transformed content),
    // which collapses that recipe's "outer Width" and "Content.Width"
    // into the same value - and re-anchoring every frame to stale
    // gesture-start state (instead of the actual scale/translation from
    // the previous frame) drifts further off with every frame the
    // gesture runs, and carries that drift into _xOffset/_yOffset for
    // the *next* gesture too. That's the compounding error behind
    // "zooming in gets harder each time" and "panning gets slower after
    // each zoom": the content silently pans out of alignment with the
    // fingers, hits the clamp bounds early, and has less room left each
    // time.
    //
    // Replaced with a direct per-frame derivation: e.ScaleOrigin is a
    // [0,1] fraction of PageContainer's own unscaled local size (per the
    // same MAUI source, independent of current Scale/Translation), so
    // the local pinch point is `e.ScaleOrigin * PageContainer.Width/Height`
    // regardless of zoom level. With AnchorX/AnchorY = 0, a local point
    // lx renders on screen at `TranslationX + lx * Scale` (relative to
    // PageContainer's constant layout position), so keeping that exact
    // point stationary as Scale moves from the *live* previousScale to
    // the new _currentScale solves to:
    //   TranslationX_new = TranslationX_prev - lx * (currentScale - previousScale)
    // Anchoring to the live PageContainer.Scale/TranslationX (read fresh
    // every frame) instead of a per-gesture snapshot means there's
    // nothing left to drift.
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            PageContainer.AnchorX = 0;
            PageContainer.AnchorY = 0;
            HideDetail();
        }
        else if (e.Status == GestureStatus.Running)
        {
            var previousScale = _currentScale;
            _currentScale += e.Scale - 1;
            _currentScale = Math.Clamp(_currentScale, 1, MaxPagedZoom);
            ApplyPinchTranslation(previousScale, e.ScaleOrigin.X, e.ScaleOrigin.Y);
        }
        else if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _xOffset = PageContainer.TranslationX;
            _yOffset = PageContainer.TranslationY;
            ScheduleDetail();
        }
    }

    // Shared by OnPinchUpdated (iOS, via the MAUI PinchGestureRecognizer -
    // see AttachAndroidPageContainerTouchListener for why Android doesn't
    // use that path) and the Android touch listener - both have already
    // updated _currentScale by this point using their own platform's scale
    // semantics; this just applies the resulting translation.
    private void ApplyPinchTranslation(double previousScale, double originXFraction, double originYFraction)
    {
        var previousTranslationX = PageContainer.TranslationX;
        var previousTranslationY = PageContainer.TranslationY;

        var localX = originXFraction * PageContainer.Width;
        var localY = originYFraction * PageContainer.Height;
        var scaleDelta = _currentScale - previousScale;

        var targetX = previousTranslationX - localX * scaleDelta;
        var targetY = previousTranslationY - localY * scaleDelta;

        PageContainer.TranslationX = targetX;
        PageContainer.TranslationY = targetY;
        PageContainer.Scale = _currentScale;
        ClampPageTranslation();
        UpdateResizeHandleScale();
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
        if (ToolSheet.IsVisible)
        {
            // Dragging on the page while the tool panel is open closes it,
            // same as a plain tap (see OnPageContainerTapped) - don't
            // also pan/turn the page underneath.
            if (e.StatusType == GestureStatus.Completed)
            {
                ToolSheet.IsVisible = false;
            }

            return;
        }

        if (_isToolFabExpanded)
        {
            if (e.StatusType == GestureStatus.Completed)
            {
                SetToolFabExpanded(false);
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
                ApplyPanRunning(e.TotalX, e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ApplyPanEnded();
                break;
        }
    }

    // Shared by OnPanUpdated (iOS) and the Android touch listener's pan
    // path - see ApplyPinchTranslation for why the platforms split here.
    private void ApplyPanRunning(double totalX, double totalY)
    {
        if (_currentScale > ZoomedInThreshold)
        {
            HideDetail();
            PageContainer.TranslationX = _xOffset + totalX;
            PageContainer.TranslationY = _yOffset + totalY;
            ClampPageTranslation();
        }

        _panTotalX = totalX;
        _panTotalY = totalY;
    }

    private void ApplyPanEnded()
    {
        // A drag along the mode axis turns the page when the page has
        // nothing left to pan on that axis, or when the drag started at the
        // pan limit in its direction (_xOffset/_yOffset still hold the start).
        // Horizontal: swipe left = next. Vertical: swipe up = next.
        var vertical = _viewModel.IsVerticalPaged;
        var visible = EditorVisibleRect();
        var zoomed = _currentScale > ZoomedInThreshold;
        var (min, max) = !zoomed
            ? (0.0, 0.0)
            : vertical
                ? PagedViewport.Range(PageContainer.Height, _currentScale, visible.Top, visible.Bottom, FitToPageExtent)
                : PagedViewport.Range(PageContainer.Width, _currentScale, visible.Left, visible.Right, FitToPageExtent);
        var pageTurnDrag = vertical ? _panTotalY : _panTotalX;
        var crossDrag = vertical ? _panTotalX : _panTotalY;
        var intent = PagedViewport.SwipeTurn(pageTurnDrag, vertical ? _yOffset : _xOffset, min, max, PageTurnDragThreshold);
        var panning = max - min > 2 || CanPanAxis(horizontal: vertical);
        if (panning && Math.Abs(pageTurnDrag) <= Math.Abs(crossDrag))
        {
            intent = PageTurnIntent.None;
        }

        if (zoomed)
        {
            _xOffset = PageContainer.TranslationX;
            _yOffset = PageContainer.TranslationY;
            ScheduleDetail();
        }

        if (intent == PageTurnIntent.Next)
        {
            TryGoToNextPage();
        }
        else if (intent == PageTurnIntent.Previous)
        {
            // Like the pedal: back onto the end of the previous page.
            TryGoToPreviousPage(alignBottom: vertical);
        }

        _panTotalX = 0;
        _panTotalY = 0;
    }

#if ANDROID
    // Entry points for PageContainerTouchListener (Platforms/Android), the
    // unified 1-finger-pan-vs-2-finger-pinch touch handler that replaces
    // PageContainer's MAUI GestureRecognizers on Android only - see
    // AttachAndroidPageContainerTouchListener for why. These mirror
    // OnPinchUpdated/OnPanUpdated/OnPageContainerTapped's own guard
    // clauses (ToolSheet dismissal, Eraser bailout) so behavior matches
    // the iOS path exactly; only the gesture *source* differs.

    private void AndroidHandlePinchStarted()
    {
        PageContainer.AnchorX = 0;
        PageContainer.AnchorY = 0;
        HideDetail();
    }

    // rawScaleFactor is PageContainerTouchListener's own hand-rolled
    // per-frame span ratio (stable-coordinate-based, not Android's
    // ScaleGestureDetector - see that class's comment for why), with none
    // of MAUI's PinchGestureHandler pre-multiplication (that class is
    // bypassed entirely on Android now). Composing it multiplicatively
    // (_currentScale *= rawScaleFactor) is the exact, not approximated,
    // equivalent of the +(e.Scale-1) summation OnPinchUpdated uses.
    private void AndroidHandlePinchRunning(double rawScaleFactor, double originXFraction, double originYFraction)
    {
        var previousScale = _currentScale;
        _currentScale = Math.Clamp(_currentScale * rawScaleFactor, 1, MaxPagedZoom);
        ApplyPinchTranslation(previousScale, originXFraction, originYFraction);
    }

    private void AndroidHandlePinchEnded()
    {
        _xOffset = PageContainer.TranslationX;
        _yOffset = PageContainer.TranslationY;
        ScheduleDetail();
    }

    private void AndroidHandlePanRunning(double totalXDp, double totalYDp)
    {
        if (ToolSheet.IsVisible || _isToolFabExpanded || _viewModel.ActiveTool == AnnotationTool.Eraser)
        {
            return;
        }

        ApplyPanRunning(totalXDp, totalYDp);
    }

    private void AndroidHandlePanEnded()
    {
        if (ToolSheet.IsVisible)
        {
            ToolSheet.IsVisible = false;
            return;
        }

        if (_isToolFabExpanded)
        {
            SetToolFabExpanded(false);
            return;
        }

        if (_viewModel.ActiveTool == AnnotationTool.Eraser)
        {
            return;
        }

        ApplyPanEnded();
    }

    private void AndroidHandleTap(double normalizedX, double normalizedY)
    {
        if (ToolSheet.IsVisible)
        {
            ToolSheet.IsVisible = false;
            return;
        }

        if (_isToolFabExpanded)
        {
            SetToolFabExpanded(false);
            return;
        }

        if (PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        HandleContainerTap(normalizedX, normalizedY);
    }

    // PageContainer keeps its XAML-declared Tap/Pinch/PanGestureRecognizers
    // for iOS (OnPinchUpdated/OnPanUpdated/OnPageContainerTapped above),
    // where this bug wasn't reported. On Android, stacking Pinch and Pan on
    // the same element is a known gesture-arena race - see the
    // OnPanUpdated/Swipe comment for the same problem with a different
    // recognizer pair - and unlike that fix (just deleting Swipe), MAUI's
    // Pinch/PanGestureRecognizer give no way to set recognition priority or
    // even see how many fingers are down, so it can't be solved from C#
    // alone. Clearing GestureRecognizers here stops MAUI from installing
    // its own Android touch listener on PageContainer at all, so the
    // listener attached below - built on Android's own ScaleGestureDetector
    // plus manual pointer-count tracking - is the sole source of touch
    // handling for this element on Android, routing 1-finger-throughout
    // sequences to pan/tap and 2-finger sequences to pinch deterministically.
    private PiccoloReader.Platforms.Android.PageContainerTouchListener? _pageContainerTouchListener;

    // See ContinuousZoomTouchListener. The CollectionView's platform view is
    // its RecyclerView.
    private void AttachAndroidContinuousZoomListener()
    {
        ContinuousPagesView.HandlerChanged += (_, _) =>
        {
            if (ContinuousPagesView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
            {
                _continuousRecyclerView = recyclerView;
                _continuousZoomTouchListener = new PiccoloReader.Platforms.Android.ContinuousZoomTouchListener(
                    recyclerView.Context!,
                    onPinchStart: ContinuousPinchStarted,
                    onPinchUpdate: ContinuousPinchRunning,
                    onPinchEnd: ContinuousPinchEnded,
                    onHorizontalPan: ContinuousPanHorizontally);
                _continuousZoomTouchListener.Stylus = CreateContinuousStylusHandler();
                recyclerView.AddOnItemTouchListener(_continuousZoomTouchListener);
            }
        };
    }

    private void AttachAndroidPageContainerTouchListener()
    {
        PageContainer.GestureRecognizers.Clear();
        PageContainer.HandlerChanged += (_, _) =>
        {
            if (PageContainer.Handler?.PlatformView is global::Android.Views.View platformView)
            {
                _pageContainerTouchListener = new PiccoloReader.Platforms.Android.PageContainerTouchListener(
                    platformView.Context!,
                    onTap: AndroidHandleTap,
                    onPinchStart: AndroidHandlePinchStarted,
                    onPinchUpdate: AndroidHandlePinchRunning,
                    onPinchEnd: AndroidHandlePinchEnded,
                    onPanUpdate: AndroidHandlePanRunning,
                    onPanEnd: AndroidHandlePanEnded);
                _pageContainerTouchListener.Stylus = CreateStylusHandler(nativePencil: false);
                platformView.SetOnTouchListener(_pageContainerTouchListener);
            }
        };
    }

    // Two-finger pinch zooms the page while Pencil is active - see
    // DrawingPinchTouchListener. The stroke the first finger started is
    // discarded (OnPencilDrawingLineCompleted checks the flag).
    private PiccoloReader.Platforms.Android.DrawingPinchTouchListener? _pencilPinchTouchListener;

    private void AttachAndroidPencilPinchListener()
    {
        PencilDrawingView.HandlerChanged += (_, _) =>
        {
            if (PencilDrawingView.Handler?.PlatformView is global::Android.Views.View platformView)
            {
                _pencilPinchTouchListener = new PiccoloReader.Platforms.Android.DrawingPinchTouchListener(
                    onPinchBegin: () => _discardPencilLine = true,
                    pinchTarget: () =>
                        _pageContainerTouchListener is not null && PageContainer.Handler?.PlatformView is global::Android.Views.View pageView
                            ? (_pageContainerTouchListener, pageView)
                            : null);
                _pencilPinchTouchListener.Stylus = CreateStylusHandler(nativePencil: true);
                platformView.SetOnTouchListener(_pencilPinchTouchListener);
            }
        };
    }

    // Stylus input (see StylusGestureHandler). Erasing (eraser tip or side
    // button) always works while Pencil is active, and with direct drawing on
    // it also works on the plain page; there a plain stylus stroke draws
    // without entering annotation mode. Pencil mode leaves plain strokes to
    // its DrawingView.
    private PiccoloReader.Platforms.Android.StylusGestureHandler CreateStylusHandler(bool nativePencil) =>
        new(
            canDraw: () => !nativePencil && CanStylusActOnPage(),
            canErase: () => nativePencil || CanStylusActOnPage(),
            drawBegin: OnStylusDrawBegin,
            drawMove: OnStylusDrawMove,
            drawEnd: OnStylusDrawEnd,
            drawCancel: OnStylusDrawCancel,
            eraseBegin: OnStylusEraseBegin,
            eraseMove: (x, y) => EraseAtDrawingViewPoint(new PointF(x, y)),
            eraseEnd: OnStylusEraseEnd,
            onTap: nativePencil ? null : AndroidHandleTap,
            cancelOngoing: nativePencil
                ? (view, e) =>
                {
                    // Same as a pinch: end the DrawingView stroke and drop it. LineCompleted
                    // consumes the flag when it fires; if it comes later, the next
                    // LineStarted clears it, so it never discards a later stroke.
                    _discardPencilLine = true;
                    var up = global::Android.Views.MotionEvent.Obtain(e.DownTime, e.EventTime, global::Android.Views.MotionEventActions.Up, e.GetX(0), e.GetY(0), e.MetaState);
                    if (up is not null)
                    {
                        view.OnTouchEvent(up);
                        up.Recycle();
                    }
                }
                : (_, _) => _pageContainerTouchListener?.ResetGesture());

    // Direct stylus use on the plain page: setting on, no tool active and a
    // single-page view (the continuous list is not covered).
    private bool CanStylusActOnPage() =>
        _stylusPreferences.DrawsDirectly
        && _viewModel.ActiveTool == AnnotationTool.MusicIcons
        && !IsContinuousListActive
        && PageContainer.Width > 0
        && PageContainer.Height > 0
        && int.TryParse(SheetId, out _);

    // ---- Direct stylus use on the continuous list -------------------------
    // The stroke is bound to the list item under the first touch point. It
    // is stored on that ContinuousPage without opening the editor (no visible
    // mode switch, no undo entry). Everything is in the unzoomed dp space of
    // the list itself, so the current zoom and scroll are already applied.
    private ContinuousPage? _stylusContinuousPage;
    private global::Android.Views.View? _stylusContinuousChild;
    private int _stylusContinuousPosition;
    private int _stylusContinuousTop;
    private bool _stylusContinuousDropped;
    private bool _stylusContinuousInvalidatePending;

    private PiccoloReader.Platforms.Android.StylusGestureHandler CreateContinuousStylusHandler() =>
        new(
            canDraw: CanStylusActOnContinuousList,
            canErase: CanStylusActOnContinuousList,
            drawBegin: OnContinuousStylusDrawBegin,
            drawMove: OnContinuousStylusDrawMove,
            drawEnd: OnContinuousStylusDrawEnd,
            drawCancel: OnContinuousStylusDrawCancel,
            eraseBegin: () => { },
            eraseMove: EraseAtContinuousPoint,
            eraseEnd: () => { },
            onTap: OnContinuousStylusTap,
            cancelOngoing: (_, _) => _continuousZoomTouchListener?.ResetGesture());

    private bool CanStylusActOnContinuousList() =>
        _stylusPreferences.DrawsDirectly
        && IsContinuousListActive
        && _viewModel.ActiveTool == AnnotationTool.MusicIcons
        && _continuousRecyclerView is not null
        && int.TryParse(SheetId, out _);

    private static float DensityOf(global::Android.Views.View view) =>
        view.Resources?.DisplayMetrics?.Density ?? 1f;

    // The list item under a point (list dp), its page and the point mapped
    // into page-normalized coordinates.
    private (ContinuousPage Page, global::Android.Views.View Child, int Position, StrokePoint Point, double ItemWidth)? ContinuousPageAt(float xDp, float yDp)
    {
        if (_continuousRecyclerView is not { } rv)
        {
            return null;
        }

        var density = DensityOf(rv);
        var child = rv.FindChildViewUnder(xDp * density, yDp * density);
        return child is null ? null : ContinuousPageInChild(rv, child, xDp, yDp);
    }

    private (ContinuousPage Page, global::Android.Views.View Child, int Position, StrokePoint Point, double ItemWidth)? ContinuousPageInChild(
        AndroidX.RecyclerView.Widget.RecyclerView rv, global::Android.Views.View child, float xDp, float yDp)
    {
        var position = rv.GetChildAdapterPosition(child);
        if (position < 0 || position >= _viewModel.ContinuousPages.Count)
        {
            return null;
        }

        var page = _viewModel.ContinuousPages[position];
        var density = DensityOf(rv);
        var itemWidth = child.Width / density;
        var point = ContinuousStylusGeometry.ToPagePoint(
            xDp, yDp, child.Left / density, child.Top / density, itemWidth, child.Height / density - ContinuousPageView.Spacing,
            page.FullAspectRatio, page.Crop);
        return point is { } p ? (page, child, position, p, itemWidth) : null;
    }

    private ContinuousPageView? ViewOfContinuousPage(ContinuousPage page)
    {
        foreach (var reference in _continuousViews)
        {
            if (reference.TryGetTarget(out var view) && ReferenceEquals(view.Page, page))
            {
                return view;
            }
        }

        return null;
    }

    private void OnContinuousStylusDrawBegin(float x, float y)
    {
        _stylusLiveStroke = new List<StrokePoint>();
        _stylusContinuousDropped = false;
        _stylusContinuousPage = null;
        _ = int.TryParse(SheetId, out _stylusSheetId);
        if (ContinuousPageAt(x, y) is not { } hit)
        {
            _stylusContinuousDropped = true;
            return;
        }

        _stylusContinuousPage = hit.Page;
        _stylusContinuousChild = hit.Child;
        _stylusContinuousPosition = hit.Position;
        _stylusContinuousTop = hit.Child.Top;
        _stylusLiveStroke.Add(hit.Point);
        ViewOfContinuousPage(hit.Page)?.SetLiveStroke(_stylusLiveStroke, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth);
    }

    private void OnContinuousStylusDrawMove(float x, float y)
    {
        if (_stylusContinuousPage is not { } page
            || _stylusContinuousChild is not { } child
            || _stylusContinuousDropped
            || _continuousRecyclerView is not { } rv)
        {
            return;
        }

        // The list scrolled (pedal, fling) or the item was recycled: the
        // stroke no longer lines up with the page, so it is dropped.
        if (child.Top != _stylusContinuousTop || rv.GetChildAdapterPosition(child) != _stylusContinuousPosition)
        {
            _stylusContinuousDropped = true;
            _stylusLiveStroke = new List<StrokePoint>();
            ViewOfContinuousPage(page)?.SetLiveStroke(null, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth);
            return;
        }

        if (ContinuousPageInChild(rv, child, x, y) is not { } hit)
        {
            return;
        }

        if (_stylusLiveStroke.Count > 0 && _stylusLiveStroke[^1] == hit.Point)
        {
            return;
        }

        _stylusLiveStroke.Add(hit.Point);
        if (_stylusContinuousInvalidatePending)
        {
            return;
        }

        // At most one repaint per frame.
        _stylusContinuousInvalidatePending = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
        {
            _stylusContinuousInvalidatePending = false;
            ViewOfContinuousPage(page)?.InvalidateAnnotations();
        });
    }

    private void OnContinuousStylusDrawCancel()
    {
        var page = _stylusContinuousPage;
        _stylusContinuousPage = null;
        _stylusContinuousChild = null;
        _stylusLiveStroke = new List<StrokePoint>();
        if (page is not null)
        {
            ViewOfContinuousPage(page)?.SetLiveStroke(null, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth);
        }
    }

    private async void OnContinuousStylusDrawEnd()
    {
        var page = _stylusContinuousPage;
        var stroke = _stylusLiveStroke;
        var sheetId = _stylusSheetId;
        var dropped = _stylusContinuousDropped;
        _stylusContinuousPage = null;
        _stylusContinuousChild = null;
        try
        {
            if (page is not null && !dropped && stroke.Count >= 2 && sheetId > 0)
            {
                await _viewModel.AddStrokeToContinuousPageAsync(page, sheetId, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, stroke);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Continuous stylus stroke save failed: {ex}");
        }
        finally
        {
            // The live stroke stays until the saved one has replaced it.
            if (page is not null)
            {
                ViewOfContinuousPage(page)?.SetLiveStroke(null, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth);
            }

            if (ReferenceEquals(stroke, _stylusLiveStroke))
            {
                _stylusLiveStroke = new List<StrokePoint>();
            }
        }
    }

    private void EraseAtContinuousPoint(float xDp, float yDp)
    {
        if (ContinuousPageAt(xDp, yDp) is not { } hit || !hit.Page.Crop.Contains(hit.Point.X, hit.Point.Y))
        {
            return;
        }

        var annotation = FindErasableAnnotation(hit.Page.Annotations, hit.Point.X, hit.Point.Y, hit.Page.FullAspectRatio, _viewModel.EraserRadius);
        if (annotation is not null)
        {
            _ = EraseContinuousAnnotationSafeAsync(hit.Page, annotation);
        }
    }

    private async Task EraseContinuousAnnotationSafeAsync(ContinuousPage page, Annotation annotation)
    {
        try
        {
            await _viewModel.EraseContinuousAnnotationAsync(page, annotation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Continuous stylus erase failed: {ex}");
        }
    }

    // A stylus tap acts like a finger tap on the page under it.
    private void OnContinuousStylusTap(double xFraction, double yFraction)
    {
        if (_continuousRecyclerView is not { } rv)
        {
            return;
        }

        var density = DensityOf(rv);
        var xDp = (float)(xFraction * rv.Width / density);
        var yDp = (float)(yFraction * rv.Height / density);
        if (ContinuousPageAt(xDp, yDp) is { } hit && hit.ItemWidth > 0)
        {
            OnContinuousPageTapped(hit.Page, hit.Point.X, hit.Point.Y, xDp / hit.ItemWidth);
        }
    }

    // SelectionBorder (icon move) and ResizeHandle (icon resize) are both
    // nested inside PageContainer's live zoom transform - and, one level
    // closer, inside SelectionOverlay's own TranslationX/Y, which is
    // itself repositioned every Running frame to follow the drag. MAUI's
    // PanGestureRecognizer.TotalX/TotalY, despite the "raw screen pixels"
    // assumption the original (pre-fix) code and comments here relied on,
    // turned out not to hold on Android for a recognizer this deeply
    // nested - confirmed on a real device (icon dragging felt
    // significantly slower than the finger once zoomed in) and reproduced
    // with instrumented logging (the same physical drag measured smaller
    // as zoom increased). SingleDragTouchListener replaces it with
    // MotionEvent.RawX/RawY, which are always true screen coordinates
    // regardless of transform nesting depth - see that class's comment.
    private PiccoloReader.Platforms.Android.SingleDragTouchListener? _selectionMoveTouchListener;
    private PiccoloReader.Platforms.Android.SingleDragTouchListener? _selectionResizeTouchListener;

    private void AttachAndroidSelectionDragListeners()
    {
        SelectionBorder.GestureRecognizers.Clear();
        SelectionBorder.HandlerChanged += (_, _) =>
        {
            if (SelectionBorder.Handler?.PlatformView is global::Android.Views.View platformView)
            {
                _selectionMoveTouchListener = new PiccoloReader.Platforms.Android.SingleDragTouchListener(
                    platformView.Context!,
                    onStarted: HandleSelectionMoveStarted,
                    onRunning: HandleSelectionMoveRunning,
                    onEnded: () => _ = HandleSelectionMoveEndedAsync());
                platformView.SetOnTouchListener(_selectionMoveTouchListener);
            }
        };

        ResizeHandle.GestureRecognizers.Clear();
        ResizeHandle.HandlerChanged += (_, _) =>
        {
            if (ResizeHandle.Handler?.PlatformView is global::Android.Views.View platformView)
            {
                _selectionResizeTouchListener = new PiccoloReader.Platforms.Android.SingleDragTouchListener(
                    platformView.Context!,
                    onStarted: HandleSelectionResizeStarted,
                    onRunning: HandleSelectionResizeRunning,
                    onEnded: HandleSelectionResizeEnded);
                platformView.SetOnTouchListener(_selectionResizeTouchListener);
            }
        };
    }
#endif

    // Shows the eraser's hit-test radius centered on the current touch
    // point, hiding itself 350ms after the most recent call - during a
    // drag (see the EraserDrawingView handlers below), each new point
    // restarts the timer, so the indicator tracks the finger live and
    // only fades out shortly after it lifts.
    private void ShowEraserRadiusIndicator(double localX, double localY)
    {
        var radiusPx = _viewModel.EraserRadius * EditorPageRect().Width;
        EraserRadiusIndicator.WidthRequest = radiusPx * 2;
        EraserRadiusIndicator.HeightRequest = radiusPx * 2;
        EraserRadiusIndicator.TranslationX = localX - radiusPx;
        EraserRadiusIndicator.TranslationY = localY - radiusPx;
        EraserRadiusIndicator.IsVisible = true;

        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(350), () =>
        {
            EraserRadiusIndicator.IsVisible = false;
            return false;
        });
    }

    // The first annotation within the eraser radius of a page-normalized
    // point. aspect is the full page height / width.
    private static Annotation? FindErasableAnnotation(IEnumerable<Annotation> annotations, double normalizedX, double normalizedY, double aspect, double radius) =>
        annotations.FirstOrDefault(a =>
        {
            if (a.IsStroke)
            {
                var points = AnnotationService.DeserializePoints(a.Points);
                return StrokeHitTester.DistanceToPolyline(normalizedX, normalizedY, points, aspect) <= radius;
            }

            var radiusY = radius / aspect;
            return normalizedX >= a.X - radius && normalizedX <= a.X + a.Width + radius &&
                   normalizedY >= a.Y - radiusY && normalizedY <= a.Y + a.Height + radiusY;
        });

    // localX/localY are PageContainer-local. The radius is a fraction of the
    // page's width, so hit-testing happens in page-width units (Y is scaled
    // by the page's height/width) to keep the eraser circular.
    private void EraseAt(double localX, double localY)
    {
        ShowEraserRadiusIndicator(localX, localY);

        var pageRect = EditorPageRect();
        var aspect = pageRect.Height / pageRect.Width;
        var normalizedX = (localX - pageRect.Left) / pageRect.Width;
        var normalizedY = (localY - pageRect.Top) / pageRect.Height;
        var radius = _viewModel.EraserRadius;
        if (!_viewModel.CurrentPageCrop.Contains(normalizedX, normalizedY))
        {
            return;
        }

        var hit = FindErasableAnnotation(_viewModel.CurrentPageAnnotations, normalizedX, normalizedY, aspect, radius);

        if (hit is not null)
        {
            _ = _viewModel.EraseAnnotationAsync(hit);
            InvalidateAnnotations();
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
        _viewModel.BeginEraseBatch();

        // Closes on the initial touch-down, not when the finger lifts, so
        // the panel gets out of the way as soon as the user starts erasing
        // instead of staying open over the page for the whole drag.
        ToolSheet.IsVisible = false;
        EraseAtDrawingViewPoint(e.Point);
    }

    private void OnEraserPointDrawn(object? sender, PointDrawnEventArgs e)
    {
        EraseAtDrawingViewPoint(e.Point);
    }

    private void OnEraserDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        _viewModel.EndEraseBatch();
        EraserRadiusIndicator.IsVisible = false;
    }

    private void OnEraserDrawingLineCancelled(object? sender, EventArgs e)
    {
        _viewModel.EndEraseBatch();
        EraserRadiusIndicator.IsVisible = false;
    }

    private void EraseAtDrawingViewPoint(PointF point)
    {
        if (PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        EraseAt(point.X, point.Y);
    }

    // Bluetooth pedal / keyboard page turn (see IPageTurnInput). Paged modes
    // reuse the tap/swipe path, which also resets any zoom; the continuous
    // list scrolls by a viewport (or to the next page) instead. Returns
    // false to let the key through to whatever else is on screen.
    private bool OnPageTurnRequested(PageTurnIntent intent)
    {
        if (!CanAcceptPageTurn)
        {
            return false;
        }

        if (!_viewModel.IsContinuousReading)
        {
            // A page taller than the screen (landscape fit-width, or zoomed)
            // is read in overlapping chunks before the page itself turns.
            var visible = EditorVisibleRect();
            var result = PagedViewport.PedalTurn(
                PageContainer.TranslationY, PageContainer.Height, _currentScale, visible.Top, visible.Bottom, intent);
            if (result.Action == PagedScrollAction.ScrollTo)
            {
                HideDetail();
                PageContainer.TranslationY = PagedViewport.Clamp(
                    result.Translation, PageContainer.Height, _currentScale, visible.Top, visible.Bottom, FitToPageExtent);
                _yOffset = PageContainer.TranslationY;
                UpdateSelectionOverlay();
                ScheduleDetail();
                return true;
            }

            // At the first/last page this is a consumed no-op, so a pedal
            // press never activates a focused control.
            if (intent == PageTurnIntent.Next)
            {
                TryGoToNextPage();
            }
            else
            {
                TryGoToPreviousPage(alignBottom: true);
            }

            return true;
        }

        return ContinuousPageTurnBy(intent);
    }

    // False while something else owns the keys: annotation editing (the tool
    // sheet included), the add-bookmark sheet, the crop overlay, or no
    // document loaded. (Dialogs, action sheets and the tutorial are separate
    // windows/pages and never reach the viewer.)
    private bool CanAcceptPageTurn =>
        !_viewModel.IsContinuousEditing
        && !IsEditing
        && _viewModel.PageCount > 0
        && !AddBookmarkOverlay.IsVisible
        && !CropOverlay.IsVisible;

    private void TryGoToNextPage()
    {
        if (_viewModel.NextPageCommand.CanExecute(null))
        {
            var carry = CaptureZoomCarry();
            _viewModel.NextPageCommand.Execute(null);
            ResetZoomForNewPage(alignBottom: false, carry);
        }
    }

    private void TryGoToPreviousPage(bool alignBottom = false)
    {
        if (_viewModel.PreviousPageCommand.CanExecute(null))
        {
            var carry = CaptureZoomCarry();
            _viewModel.PreviousPageCommand.Execute(null);
            ResetZoomForNewPage(alignBottom, carry);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            if (IsEditing || _inEditSession)
            {
                await HandleEditingBackAsync();
                return;
            }

            if (_isToolFabExpanded)
            {
                SetToolFabExpanded(false);
                return;
            }

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Back failed: {ex}");
        }
    }

    // While editing, Back undoes the changes made since edit mode was
    // activated one by one and, once none are left, leaves edit mode.
    // Otherwise an expanded tool FAB menu is collapsed before leaving.
    protected override bool OnBackButtonPressed()
    {
        if (!IsEditing && !_inEditSession)
        {
            if (_isToolFabExpanded)
            {
                SetToolFabExpanded(false);
                return true;
            }

            return base.OnBackButtonPressed();
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await HandleEditingBackAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back while editing failed: {ex}");
            }
        });
        return true;
    }

    private async Task HandleEditingBackAsync()
    {
        if (_viewModel.HasEditSessionChanges && _viewModel.UndoCommand.CanExecute(null))
        {
            await _viewModel.UndoCommand.ExecuteAsync(null);
            InvalidateAnnotations();
            UpdateSelectionOverlay();

            if (!IsEditing)
            {
                // Undo cleared the selection of a hidden-sheet symbol session:
                // bring the editor back so the user still sees edit mode.
                await ShowToolSheetAsync();
            }

            return;
        }

        await ExitEditModeAsync();
    }

    // Secondary actions of the reading bar. Undo/redo live in the editing bar
    // while editing; this menu offers them for the reading state.
    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        var undo = AppStrings.UndoAction;
        var redo = AppStrings.RedoAction;

        var crop = AppStrings.CropPage;
        var share = AppStrings.Share;
        var options = new List<string> { undo, redo, crop };
        if (_shareFlow.IsSupported)
        {
            options.Add(share);
        }

        var choice = await DisplayActionSheetAsync(AppStrings.MoreOptions, AppStrings.Cancel, null, options.ToArray());
        if (choice == undo)
        {
            if (_viewModel.CanUndo)
            {
                OnUndoClicked(sender, e);
            }
        }
        else if (choice == redo)
        {
            if (_viewModel.CanRedo)
            {
                OnRedoClicked(sender, e);
            }
        }
        else if (choice == crop)
        {
            await BeginCropAsync();
        }
        else if (choice == share && int.TryParse(SheetId, out var shareSheetId))
        {
            var sheet = await _libraryService.GetSheetAsync(shareSheetId);
            await _shareFlow.ShareAsync(this, sheet, LegacyPortraitReadingArea());
        }
    }

    // Crop mode for the page being read (the current page in every reading
    // mode). The page is rendered whole for the user to crop from; the result
    // is saved by the view model, which re-renders the page cropped.
    private int _cropPageIndex;

    private async Task BeginCropAsync()
    {
        if (_viewModel.IsContinuousEditing || IsEditing || _viewModel.PageCount <= 0)
        {
            return;
        }

        _cropPageIndex = _viewModel.CurrentPageIndex;
        var bytes = await _viewModel.RenderUncroppedPageAsync(_cropPageIndex);
        if (bytes is null || !PngDimensions.TryRead(bytes, out var width, out var height))
        {
            return;
        }

        await EnsureBravuraTypefaceLoadedAsync();
        CropOverlay.Show(bytes, (double)height / width, _viewModel.GetPageCrop(_cropPageIndex), _annotationPainter,
            _viewModel.IsContinuousReading && _cropPageIndex < _viewModel.ContinuousPages.Count
                ? _viewModel.ContinuousPages[_cropPageIndex].Annotations
                : _viewModel.CurrentPageAnnotations);
    }

    private async void OnCropApplied(object? sender, PageCrop crop)
    {
        await _viewModel.SetPageCropAsync(_cropPageIndex, crop);
        ResetZoom();
        InvalidateContinuousAnnotations();
    }

    private void OnCropCancelled(object? sender, EventArgs e)
    {
    }

    private void UpdateReadingModeName()
    {
        ReadingModeNameLabel.Text = _viewModel.ReadingMode switch
        {
            ReadingMode.VerticalPaged => AppStrings.ReadingModeNameVerticalPaged,
            ReadingMode.VerticalContinuous => AppStrings.ReadingModeNameVerticalContinuous,
            _ => AppStrings.ReadingModeNameHorizontal
        };
    }

    // Bookmark icon is tinted with the Bookmark token while the current
    // page is bookmarked, otherwise it uses the regular icon color.
    private void UpdateBookmarkButton()
    {
        var bookmarked = _viewModel.Bookmarks.Any(b => b.PageIndex == _viewModel.CurrentPageIndex);
        var source = new FontImageSource
        {
            Glyph = bookmarked ? "" : "",
            FontFamily = "MaterialOutlined",
            Size = 24
        };
        if (bookmarked)
        {
            source.SetAppThemeColor(FontImageSource.ColorProperty,
                (Color)Application.Current!.Resources["BookmarkLight"],
                (Color)Application.Current!.Resources["BookmarkDark"]);
        }
        else
        {
            source.SetAppThemeColor(FontImageSource.ColorProperty,
                (Color)Application.Current!.Resources["TextPrimaryLight"],
                (Color)Application.Current!.Resources["TextPrimaryDark"]);
        }

        BookmarkButton.Source = source;
    }

    private void UpdateReadingModeIcon()
    {
        // swap_horiz / swap_vert / view_agenda - shows the mode in use.
        ReadingModeIcon.Glyph = _viewModel.ReadingMode switch
        {
            ReadingMode.VerticalPaged => "\uE8D5",
            ReadingMode.VerticalContinuous => "\uE8E9",
            _ => "\uE8D4"
        };
    }

    // Runs when the reading mode changes. Any open panel, tool or selection
    // is closed, and the continuous list starts unzoomed at the current
    // page.
    private void UpdateReadingModeUi()
    {
        _inEditSession = false;
        UpdateReadingModeIcon();
        UpdateReadingModeName();
        UpdateBookmarkButton();
        ResetContinuousZoom();

        if (_viewModel.IsContinuousReading)
        {
            ToolSheet.IsVisible = false;
            SetToolFabExpanded(false);
            _viewModel.SelectedAnnotation = null;
            _viewModel.ActiveTool = AnnotationTool.MusicIcons;
            UpdateToolSections();
            UpdateSelectionOverlay();
            ResetZoom();
            ScrollContinuousToCurrentPage();
        }
        else
        {
            InvalidateAnnotations();
        }

        UpdateContinuousLayers();
    }

    // In continuous mode the list is shown, except while one of its pages
    // is open in the single-page editor (PageContainer), which then gets a
    // "done" button to return to the list.
    private void UpdateContinuousLayers()
    {
        var continuous = _viewModel.IsContinuousReading;
        var editing = continuous && _viewModel.IsContinuousEditing;

        PageContainer.IsVisible = !continuous || editing;
        ContinuousHost.IsVisible = continuous && !editing;
        UpdateEditingUi();
    }

    // Dispatched so it runs after the list has become visible and laid
    // out - ScrollTo on a list that hasn't been measured yet is ignored.
    private void ScrollContinuousToCurrentPage()
    {
        var pageIndex = _viewModel.CurrentPageIndex;
        Dispatcher.Dispatch(() =>
        {
            if (_viewModel.IsContinuousReading && pageIndex < _viewModel.ContinuousPages.Count)
            {
                ContinuousPagesView.ScrollTo(pageIndex, position: ScrollToPosition.Start, animate: false);
            }
        });
    }

    private async void OnContinuousPagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _continuousScrollOffsetFallback = e.VerticalOffset;
        if (IsContinuousListActive)
        {
            ScheduleDetail();
        }

        await UpdateContinuousViewportAsync();
    }

    // Works out the visible pages from the scroll offset and page sizes
    // rather than the list's own First/Center/LastVisibleItemIndex: while
    // zoomed, only the top 1/zoom of the list's viewport is on screen, which
    // the list itself doesn't know about.
    private Task UpdateContinuousViewportAsync()
    {
        var width = ContinuousPagesView.Width;
        var height = ContinuousPagesView.Height;
        if (!_viewModel.IsContinuousReading || _viewModel.IsContinuousEditing || width <= 0 || height <= 0)
        {
            return Task.CompletedTask;
        }

        var pages = _viewModel.ContinuousPages;
        var top = ContinuousScrollOffset();
        var visibleHeight = height / _continuousZoom;

        return _viewModel.UpdateContinuousViewportAsync(
            ContinuousLayout.PageAt(pages, top, width, ContinuousPageView.Spacing),
            ContinuousLayout.PageAt(pages, top + visibleHeight, width, ContinuousPageView.Spacing),
            ContinuousLayout.PageAt(pages, top + visibleHeight / 2, width, ContinuousPageView.Spacing));
    }

    // Content offset (dp) at the top of the list's viewport. On Android this
    // is derived from the first visible item's actual position, because the
    // Scrolled event's VerticalOffset is a running sum of scroll deltas that
    // goes stale after a non-animated ScrollTo (page jumps, bookmarks).
    private double ContinuousScrollOffset()
    {
#if ANDROID
        if (_continuousRecyclerView?.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layoutManager)
        {
            var position = layoutManager.FindFirstVisibleItemPosition();
            var child = position >= 0 ? layoutManager.FindViewByPosition(position) : null;
            if (child is not null && position < _viewModel.ContinuousPages.Count)
            {
                var density = DeviceDisplay.Current.MainDisplayInfo.Density;
                return ContinuousLayout.PageTop(_viewModel.ContinuousPages, position, ContinuousPagesView.Width, ContinuousPageView.Spacing)
                    - child.Top / density;
            }
        }
#endif
        return _continuousScrollOffsetFallback;
    }

    // A tap on an annotation opens that page in the editor with the
    // annotation selected (to move, resize or delete it); anywhere else
    // toggles the toolbar, like a center tap in the paged modes.
    private async void OnContinuousPageTapped(ContinuousPage page, double normalizedX, double normalizedY, double listLocalX)
    {
#if ANDROID
        // A pan/scroll that ends over the page is not a tap (see IsTap).
        if (_continuousZoomTouchListener is { IsTap: false })
        {
            return;
        }
#endif

        if (ToolSheet.IsVisible)
        {
            ToolSheet.IsVisible = false;
            return;
        }

        if (_isToolFabExpanded)
        {
            SetToolFabExpanded(false);
            return;
        }

        // Strokes are stored without bounds (X/Y/Width/Height stay 0), so
        // they're hit by distance to their points. They have no selection
        // box, so the editor just opens (to erase or redraw).
        var hit = page.Annotations.FirstOrDefault(a => !a.IsStroke &&
            normalizedX >= a.X && normalizedX <= a.X + a.Width &&
            normalizedY >= a.Y && normalizedY <= a.Y + a.Height);

        if (hit is not null)
        {
            await EnterContinuousEditAsync(page.PageIndex, hit.Id);
            return;
        }

        var strokeHit = page.Annotations.Any(a => a.IsStroke &&
            StrokeHitTester.DistanceToPolyline(normalizedX, normalizedY, AnnotationService.DeserializePoints(a.Points), page.FullAspectRatio)
                <= StrokeTapTolerance + a.StrokeWidth / 2);

        if (strokeHit)
        {
            await EnterContinuousEditAsync(page.PageIndex);
            return;
        }

        // Side zones, measured on screen (the list may be zoomed and panned):
        // the tap's list-local X is mapped through the list's zoom/translation.
        var hostWidth = ContinuousHost.Width;
        if (hostWidth > 0)
        {
            var screenX = (ContinuousPagesView.TranslationX + listLocalX * ContinuousPagesView.Width * _continuousZoom) / hostWidth;
            if (screenX < SideTapZoneFraction)
            {
                ContinuousPageTurnBy(PageTurnIntent.Previous);
                return;
            }

            if (screenX > 1 - SideTapZoneFraction)
            {
                ContinuousPageTurnBy(PageTurnIntent.Next);
                return;
            }
        }

        SetToolbarVisible(!_isToolbarVisible);
    }

    // Pedal / side-tap page turn of the continuous list: one viewport (or to
    // the next page) at a time.
    private bool ContinuousPageTurnBy(PageTurnIntent intent)
    {
        var width = ContinuousPagesView.Width;
        var height = ContinuousPagesView.Height;
        if (width <= 0 || height <= 0)
        {
            return false; // list not laid out yet
        }

        _continuousScrollRemainderPx = 0;
        var scroll = ContinuousScrollOffset();
        var target = ContinuousPageTurn.TargetOffset(
            _viewModel.ContinuousPages, width, ContinuousPageView.Spacing, scroll, height / _continuousZoom, intent);
        if (target is { } offset)
        {
            ScrollContinuousBy(offset - scroll);
        }

        return true;
    }

    // Opens a page of the continuous list in the single-page editor, keeping
    // the list's zoom and position so the page doesn't jump on screen.
    private async Task EnterContinuousEditAsync(int pageIndex, int? selectAnnotationId = null)
    {
        var listWidth = ContinuousPagesView.Width;
        var listZoom = _continuousZoom;
        var listTranslationX = ContinuousPagesView.TranslationX;
        var pageScreenTop = (ContinuousLayout.PageTop(_viewModel.ContinuousPages, pageIndex, listWidth, ContinuousPageView.Spacing)
            - ContinuousScrollOffset()) * listZoom;

        await _viewModel.BeginContinuousEditAsync(pageIndex);
        UpdateContinuousLayers();

        if (selectAnnotationId is { } id)
        {
            _viewModel.SelectedAnnotation = _viewModel.CurrentPageAnnotations.FirstOrDefault(a => a.Id == id);
        }

        InvalidateAnnotations();
        ResetZoom();

        // PageContainer was hidden until now - give it a layout pass before
        // reading its size/position.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            ApplyEditorZoomFromContinuous(pageIndex, listWidth, listZoom, listTranslationX, pageScreenTop));
    }

    // Both views share the same origin (the reading area's top-left). In the
    // list the page is listWidth wide at listZoom with its left edge at
    // listTranslationX and its top at pageScreenTop; this sets
    // PageContainer's zoom/translation to put it at the same place. The
    // page sits letterboxed inside PageContainer, so its top is offset
    // from the container's by pageTopInContainer (unscaled).
    private void ApplyEditorZoomFromContinuous(int pageIndex, double listWidth, double listZoom, double listTranslationX, double pageScreenTop)
    {
        if (!_viewModel.IsContinuousEditing || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        // The page is letterboxed inside PageContainer (in landscape it is
        // narrower than the container), so the zoom is derived from the
        // page's own rectangle rather than the container's.
        // The list's row is the cropped part of the page, so it is that
        // part's rectangle which has to land on the row.
        var pageRect = EditorVisibleRect();
        var pageTopInContainer = pageRect.Top;

        var scale = listZoom * listWidth / pageRect.Width;
        if (scale <= ZoomedInThreshold)
        {
            // Not zoomed in: only shift vertically (unclamped, there is no
            // zoom range to pan within) so the page stays where it was in
            // the list instead of jumping to the centered position.
            PageContainer.TranslationY = pageScreenTop - PageContainer.Y - pageTopInContainer;
            UpdateSelectionOverlay();
            return;
        }

        _currentScale = scale;
        PageContainer.AnchorX = 0;
        PageContainer.AnchorY = 0;
        PageContainer.Scale = scale;
        PageContainer.TranslationX = Math.Clamp(listTranslationX - PageContainer.X - pageRect.Left * scale, -PageContainer.Width * (scale - 1), 0);
        PageContainer.TranslationY = Math.Clamp(pageScreenTop - PageContainer.Y - pageTopInContainer * scale, -PageContainer.Height * (scale - 1), 0);
        _xOffset = PageContainer.TranslationX;
        _yOffset = PageContainer.TranslationY;
        ScheduleDetail();
        UpdateResizeHandleScale();
        UpdateSelectionOverlay();
    }

    private async Task ExitContinuousEditAsync()
    {
        ToolSheet.IsVisible = false;
        SetToolFabExpanded(false);

        await _viewModel.EndContinuousEditAsync();

        UpdateToolSections();
        UpdateSelectionOverlay();
        ResetZoom();
        UpdateContinuousLayers();
        InvalidateContinuousAnnotations();
    }

    // Annotation tools picked from the FAB while scrolling the continuous
    // list edit the page currently in the middle of the screen.
    private async Task EnsureEditablePageAsync()
    {
        if (_viewModel.IsContinuousReading && !_viewModel.IsContinuousEditing)
        {
            await EnterContinuousEditAsync(_viewModel.CurrentPageIndex);
        }
    }

    private void ResetContinuousZoom()
    {
        _continuousZoom = 1;
        ContinuousPagesView.Scale = 1;
        ContinuousPagesView.TranslationX = 0;
        ContinuousZoomFooter.HeightRequest = 0;
    }

    private void ContinuousPinchStarted()
    {
        ContinuousPagesView.AnchorX = 0;
        ContinuousPagesView.AnchorY = 0;
        HideDetail();
    }

    // Horizontal: same anchored-translation math as ApplyPinchTranslation.
    // Vertical: no translation (it would uncover empty space above the
    // list) - the list is scrolled instead, by the amount that keeps the
    // row under the fingers in place: a row at viewport position y is on
    // screen at y*zoom, so going from zoom k to k' needs a scroll of
    // y*(1 - k/k').
    private void ContinuousPinchRunning(double rawScaleFactor, double focusXFraction, double focusYFraction)
    {
        var width = ContinuousPagesView.Width;
        var height = ContinuousPagesView.Height;
        var previousZoom = _continuousZoom;
        _continuousZoom = Math.Clamp(previousZoom * rawScaleFactor, 1, MaxContinuousZoom);
        if (_continuousZoom == previousZoom || width <= 0 || height <= 0)
        {
            return;
        }

        var targetX = ContinuousPagesView.TranslationX - focusXFraction * width * (_continuousZoom - previousZoom);
        ContinuousPagesView.TranslationX = Math.Clamp(targetX, -width * (_continuousZoom - 1), 0);
        ContinuousPagesView.Scale = _continuousZoom;

        // Room below the last page so it can still be scrolled fully into
        // the on-screen part of the (scaled) viewport.
        ContinuousZoomFooter.HeightRequest = height * (1 - 1 / _continuousZoom);

        ScrollContinuousBy(focusYFraction * height * (1 - previousZoom / _continuousZoom));
    }

    private async void ContinuousPinchEnded()
    {
        if (_continuousZoom <= ZoomedInThreshold)
        {
            ResetContinuousZoom();
        }

        await UpdateContinuousViewportAsync();
        ScheduleDetail();
    }

    private void ContinuousPanHorizontally(double deltaDp)
    {
        if (_continuousZoom <= ZoomedInThreshold)
        {
            return;
        }

        ContinuousPagesView.TranslationX = Math.Clamp(
            ContinuousPagesView.TranslationX + deltaDp,
            -ContinuousPagesView.Width * (_continuousZoom - 1),
            0);
        ScheduleDetail();
    }

    private void ScrollContinuousBy(double deltaDp)
    {
#if ANDROID
        if (_continuousRecyclerView is null)
        {
            return;
        }

        // RecyclerView scrolls in whole pixels - carry the fraction over so
        // many small per-frame scrolls don't drift.
        var deltaPx = deltaDp * DeviceDisplay.Current.MainDisplayInfo.Density + _continuousScrollRemainderPx;
        var wholePx = (int)Math.Truncate(deltaPx);
        _continuousScrollRemainderPx = deltaPx - wholePx;
        if (wholePx != 0)
        {
            _continuousRecyclerView.ScrollBy(0, wholePx);
        }
#endif
    }

    private async void OnReadingModeClicked(object? sender, EventArgs e)
    {
        await _viewModel.CycleReadingModeCommand.ExecuteAsync(null);

        var message = _viewModel.ReadingMode switch
        {
            ReadingMode.VerticalPaged => AppStrings.ReadingModeVerticalPagedMessage,
            ReadingMode.VerticalContinuous => AppStrings.ReadingModeVerticalContinuousMessage,
            _ => AppStrings.ReadingModeHorizontalMessage
        };
        await CommunityToolkit.Maui.Alerts.Toast.Make(message).Show();
    }

    // After jumping to a page (page indicator / bookmark). A jump while
    // editing a continuous-mode page has already closed the editor (see
    // SheetViewerViewModel.GoToPageAsync).
    private void OnJumpedToPage()
    {
        if (_viewModel.IsContinuousReading)
        {
            ToolSheet.IsVisible = false;
            UpdateToolSections();
            UpdateSelectionOverlay();
            ResetZoom();
            UpdateContinuousLayers();
            ScrollContinuousToCurrentPage();
        }
        else
        {
            // The jump already changed the page: keep the zoom on the new one.
            ResetZoomForNewPage(alignBottom: false, CaptureZoomCarry());
        }
    }

    private async void OnPageIndicatorTapped(object? sender, TappedEventArgs e)
    {
        var input = await DisplayPromptAsync(
            AppStrings.GoToPageTitle,
            string.Format(AppStrings.GoToPageMessageFormat, _viewModel.PageCount),
            accept: AppStrings.OK,
            cancel: AppStrings.Cancel,
            initialValue: _viewModel.CurrentPageDisplay.ToString(),
            keyboard: Keyboard.Numeric);

#if ANDROID
        // The dialog's keyboard leaves the window insets stale (indicator and
        // FAB end up lower, under the gesture bar) until something re-applies
        // them, same as when showing the toolbar. Redo it now and again once
        // the keyboard's hide animation is over.
        RequestAndroidWindowInsetsRefresh();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), RequestAndroidWindowInsetsRefresh);
#endif

        if (input is null)
        {
            return;
        }

        if (int.TryParse(input, out var pageNumber) && pageNumber >= 1 && pageNumber <= _viewModel.PageCount)
        {
            await _viewModel.GoToPageAsync(pageNumber - 1);
            OnJumpedToPage();
        }
    }

    private async void OnUndoClicked(object? sender, EventArgs e)
    {
        await _viewModel.UndoCommand.ExecuteAsync(null);
        // Undo/redo of a move or resize mutates the annotation in place (no
        // collection change), so repaint the glyphs explicitly.
        InvalidateAnnotations();
        UpdateSelectionOverlay();
    }

    private async void OnRedoClicked(object? sender, EventArgs e)
    {
        await _viewModel.RedoCommand.ExecuteAsync(null);
        // Undo/redo of a move or resize mutates the annotation in place (no
        // collection change), so repaint the glyphs explicitly.
        InvalidateAnnotations();
        UpdateSelectionOverlay();
    }

    // Single entry point for bookmarks, per explicit request - no separate
    // "+" toolbar icon. "Add Bookmark" is always the last option in the
    // same action sheet as the existing bookmarks, so the one button both
    // lists what's there and lets you add to it.
    private async void OnBookmarksClicked(object? sender, EventArgs e)
    {
        var addBookmarkOption = AppStrings.AddBookmarkOption;

        var ordered = _viewModel.Bookmarks.OrderBy(b => b.PageIndex).ToList();
        var options = ordered.Select(BookmarkOptionLabel).Append(addBookmarkOption).ToArray();

        var choice = await DisplayActionSheetAsync(AppStrings.BookmarksTitle, AppStrings.Cancel, null, options);

        if (choice is null || choice == AppStrings.Cancel)
        {
            return;
        }

        if (choice == addBookmarkOption)
        {
            ShowAddBookmarkOverlay();
            return;
        }

        var index = Array.IndexOf(options, choice);
        if (index >= 0 && index < ordered.Count)
        {
            await _viewModel.GoToPageAsync(ordered[index].PageIndex);
            OnJumpedToPage();
        }
    }

    private static string BookmarkOptionLabel(Bookmark bookmark) =>
        string.IsNullOrWhiteSpace(bookmark.Name)
            ? string.Format(AppStrings.BookmarkPageLabelFormat, bookmark.PageIndex + 1)
            : string.Format(AppStrings.BookmarkNamedPageLabelFormat, bookmark.Name, bookmark.PageIndex + 1);

    private void ShowAddBookmarkOverlay()
    {
        AddBookmarkPageEntry.Text = _viewModel.CurrentPageDisplay.ToString();
        AddBookmarkNameEntry.Text = string.Empty;
        AddBookmarkOverlay.IsVisible = true;
    }

    private void OnAddBookmarkNameClearClicked(object? sender, TappedEventArgs e)
    {
        AddBookmarkNameEntry.Text = string.Empty;
    }

    private void OnAddBookmarkCancelClicked(object? sender, EventArgs e)
    {
        AddBookmarkOverlay.IsVisible = false;
    }

    private async void OnAddBookmarkConfirmClicked(object? sender, EventArgs e)
    {
        if (int.TryParse(AddBookmarkPageEntry.Text, out var pageNumber) && pageNumber >= 1 && pageNumber <= _viewModel.PageCount)
        {
            var name = string.IsNullOrWhiteSpace(AddBookmarkNameEntry.Text) ? null : AddBookmarkNameEntry.Text.Trim();
            await _viewModel.AddBookmarkAsync(pageNumber - 1, name);
            AddBookmarkOverlay.IsVisible = false;
        }
    }

    // The main FAB reopens the tool sheet when a drawing tool is armed and
    // the sheet was dismissed (it hides as soon as drawing starts); otherwise
    // it toggles the speed-dial. An open sheet hides the FAB altogether.
    private async void OnMainToolFabClicked(object? sender, TappedEventArgs e)
    {
        if (_viewModel.IsDrawingToolActive && !ToolSheet.IsVisible)
        {
            SetToolFabExpanded(false);
            await ShowToolSheetAsync();
            return;
        }

        SetToolFabExpanded(!_isToolFabExpanded);
    }

    private async Task ShowToolSheetAsync()
    {
        // Load before showing: the symbol tiles paint as soon as they are
        // visible and would stay blank without the typeface.
        await EnsureBravuraTypefaceLoadedAsync();
        UpdateToolSections();
        ToolSheet.IsVisible = true;
    }

    private async void OnFabIconsClicked(object? sender, TappedEventArgs e)
    {
        await EnsureEditablePageAsync();

        _viewModel.ActiveTool = AnnotationTool.MusicIcons;
        SetToolFabExpanded(false);
        await ShowToolSheetAsync();
    }

    private async void OnFabPencilClicked(object? sender, TappedEventArgs e)
    {
        await EnsureEditablePageAsync();

        _viewModel.ActiveTool = AnnotationTool.Pencil;
        SetToolFabExpanded(false);
        await ShowToolSheetAsync();
    }

    private async void OnFabEraserClicked(object? sender, TappedEventArgs e)
    {
        await EnsureEditablePageAsync();

        _viewModel.ActiveTool = AnnotationTool.Eraser;
        SetToolFabExpanded(false);
        await ShowToolSheetAsync();
    }

    // Shows/hides the 3 mini FABs above the main FAB and swaps its own
    // icon between the pencil glyph (collapsed) and a close glyph
    // (expanded) - the collapsed/expanded state is independent of which
    // tool is active, it only tracks whether the speed-dial menu itself
    // is open.
    private void SetToolFabExpanded(bool expanded)
    {
        _isToolFabExpanded = expanded;
        IconsFab.IsVisible = expanded;
        PencilFab.IsVisible = expanded;
        EraserFab.IsVisible = expanded;
        var glyphSource = new FontImageSource
        {
            Glyph = expanded ? "" : "",
            FontFamily = "MaterialOutlined",
            Size = 24
        };
        glyphSource.SetAppThemeColor(FontImageSource.ColorProperty,
            (Color)Application.Current!.Resources["OnPrimaryLight"],
            (Color)Application.Current!.Resources["OnPrimaryDark"]);
        MainToolFabIcon.Source = glyphSource;
    }

    private void UpdateToolSections()
    {
        MusicIconsSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.MusicIcons;
        PencilSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Pencil;
        EraserSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Eraser;

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
        }

        var eraserActive = _viewModel.ActiveTool == AnnotationTool.Eraser;
        EraserDrawingView.IsVisible = eraserActive;
        if (eraserActive)
        {
            UpdateEraserDrawingViewLineWidth();
        }

        UpdateToolValueLabels();
        UpdateToolSegments();
        ApplyToolSheetHeight();
        UpdateEditingUi();
    }

    private void UpdateToolValueLabels()
    {
        PencilWidthValueLabel.Text = $"{_viewModel.PencilStrokeWidth * 1000:0.#} pt";
        EraserRadiusValueLabel.Text = $"{_viewModel.EraserRadius * 100:0.#}";
    }

    // Segmented control: the selected segment gets the PrimaryContainer
    // pill, the others stay transparent with secondary text/icons.
    private void UpdateToolSegments()
    {
        SetSegment(ToolSegmentPencil, ToolSegmentPencilLabel, ToolSegmentPencilGlyph, _viewModel.ActiveTool == AnnotationTool.Pencil);
        SetSegment(ToolSegmentEraser, ToolSegmentEraserLabel, ToolSegmentEraserGlyph, _viewModel.ActiveTool == AnnotationTool.Eraser);
        SetSegment(ToolSegmentSymbols, ToolSegmentSymbolsLabel, ToolSegmentSymbolsGlyph, _viewModel.ActiveTool == AnnotationTool.MusicIcons);
    }

    private static void SetSegment(Border segment, Label label, FontImageSource glyph, bool selected)
    {
        var res = Application.Current!.Resources;
        segment.SetAppThemeColor(VisualElement.BackgroundColorProperty,
            selected ? (Color)res["PrimaryContainerLight"] : Colors.Transparent,
            selected ? (Color)res["PrimaryContainerDark"] : Colors.Transparent);
        var light = (Color)res[selected ? "OnPrimaryContainerLight" : "TextSecondaryLight"];
        var dark = (Color)res[selected ? "OnPrimaryContainerDark" : "TextSecondaryDark"];
        label.SetAppThemeColor(Label.TextColorProperty, light, dark);
        glyph.SetAppThemeColor(FontImageSource.ColorProperty, light, dark);
    }

    // Sizes the sheet to its content. Portrait: pencil/eraser compact, the
    // symbol picker takes the rows its category needs (cap ~45%). Landscape
    // (little height, lots of width): a compact sheet (cap ~45%) with the
    // pencil colors and width side by side and the symbols in one
    // horizontally scrolling row, so the document stays usable above it.
    private void ApplyToolSheetHeight(bool animate = false)
    {
        var pageHeight = Height > 0 ? Height : 800;
        var pageWidth = Width > 0 ? Width : 400;
        var landscape = pageWidth > pageHeight;
        ApplyToolSheetLayout(landscape);
        var cap = pageHeight * 0.45;

        // Expansion only applies to the portrait symbol picker.
        if (landscape || _viewModel.ActiveTool != AnnotationTool.MusicIcons)
        {
            _toolSheetExpanded = false;
        }

        // handle + segmented control + paddings/spacing (landscape drops
        // the handle and tightens the spacing)
        var header = landscape ? 70d : 94d;
        double height;
        switch (_viewModel.ActiveTool)
        {
            case AnnotationTool.MusicIcons:
                const double tile = 64; // 56 tile + 8 margin
                if (landscape)
                {
                    // chips (36) + spacing (8) + one scrolling row (tile + padding 8 + 4)
                    height = header + 36 + 8 + tile + 12;
                    break;
                }

                var cols = Math.Max(1, (int)Math.Floor((pageWidth - 48) / tile));
                var count = MusicIconCatalog.Categories[_symbolCategoryIndex].Icons.Count;
                var rows = Math.Max(1, (int)Math.Ceiling(count / (double)cols));
                // category chips row (44) + spacing (10) + grid padding (8 + 24)
                height = Math.Min(cap, header + 44 + 10 + 40 + rows * tile);
                if (_toolSheetExpanded)
                {
                    height = pageHeight * ToolSheetExpandedFraction;
                }

                break;
            case AnnotationTool.Eraser:
                height = header + (landscape ? 82 : 100);
                break;
            default:
                // landscape: label + swatches / slider side by side + bottom padding
                height = landscape ? header + 82 : Math.Min(header + 196, Math.Max(cap, 290));
                break;
        }

        if (landscape)
        {
            height = Math.Min(height, Math.Max(cap, header + 82));
        }

        if (animate && ToolSheet.IsVisible)
        {
            AnimateToolSheetHeight(Math.Round(height));
            return;
        }

        ToolSheet.AbortAnimation("ToolSheetHeight");
        ToolSheet.TranslationY = 0;
        ToolSheet.HeightRequest = Math.Round(height);
        if (_viewModel.SelectedAnnotation is { } selected)
        {
            UpdateTrashPlacement(selected);
        }
    }

    // Symbol picker only: dragging the handle up grows the sheet to ~65% of
    // the page so more rows show; dragging down (or tapping) snaps it back.
    // Reset whenever the sheet hides or the tool leaves the symbols.
    private bool _toolSheetExpanded;
    private const double ToolSheetExpandedFraction = 0.65;

    private void OnToolSheetHandlePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType != GestureStatus.Completed)
        {
            return;
        }

        if (e.TotalY < -30)
        {
            SetToolSheetExpanded(true);
        }
        else if (e.TotalY > 30)
        {
            SetToolSheetExpanded(false);
        }
    }

    private void OnToolSheetHandleTapped(object? sender, TappedEventArgs e) =>
        SetToolSheetExpanded(!_toolSheetExpanded);

    private void SetToolSheetExpanded(bool expanded)
    {
        if (_toolSheetExpanded == expanded)
        {
            return;
        }

        _toolSheetExpanded = expanded;
        ApplyToolSheetHeight(animate: true);
    }

    private void AnimateToolSheetHeight(double target)
    {
        // Animating HeightRequest relays out the page every frame. Instead the
        // sheet is laid out at the larger of the two heights and only its
        // TranslationY moves (a compositor property); the final height is set
        // once at the end. Growing starts pushed down by the delta, shrinking
        // slides down by it, so the top edge travels the same distance.
        ToolSheet.AbortAnimation("ToolSheetHeight");
        ToolSheet.TranslationY = 0;
        var from = ToolSheet.HeightRequest;
        var delta = target - from;
        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        double start, end;
        if (delta > 0)
        {
            ToolSheet.HeightRequest = target;
            start = delta;
            end = 0;
        }
        else
        {
            start = 0;
            end = -delta;
        }

        ToolSheet.Animate("ToolSheetHeight", v => ToolSheet.TranslationY = v, start, end, length: 180, easing: Easing.CubicOut,
            finished: (_, cancelled) =>
            {
                if (cancelled)
                {
                    return;
                }

                ToolSheet.HeightRequest = target;
                ToolSheet.TranslationY = 0;
                if (_viewModel.SelectedAnnotation is { } selected)
                {
                    UpdateTrashPlacement(selected);
                }
            });
    }

    private bool? _toolSheetLandscape;

    private void ApplyToolSheetLayout(bool landscape)
    {
        if (_toolSheetLandscape == landscape)
        {
            return;
        }

        _toolSheetLandscape = landscape;
        ToolSheetHandleArea.IsVisible = !landscape;
        ToolSheetGrid.RowSpacing = landscape ? 8 : 14;
        ToolSheet.Padding = landscape ? new Thickness(20, 8, 20, 0) : new Thickness(20, 10, 20, 0);
        ToolOptionsPad.Padding = new Thickness(0, 0, 0, landscape ? 8 : 24);

        // Pencil: color block and width block share one row in landscape.
        Grid.SetRow(PencilWidthBlock, landscape ? 0 : 1);
        Grid.SetColumn(PencilWidthBlock, landscape ? 1 : 0);
        PencilSection.ColumnDefinitions = landscape
            ? new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            : new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };

        // Symbols: a single horizontally scrolling row in landscape.
        SymbolGridScroll.Orientation = landscape ? ScrollOrientation.Horizontal : ScrollOrientation.Vertical;
        SymbolGridScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
        SymbolGridScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
        SymbolGrid.Wrap = landscape ? Microsoft.Maui.Layouts.FlexWrap.NoWrap : Microsoft.Maui.Layouts.FlexWrap.Wrap;
        SymbolGridPad.Padding = landscape ? new Thickness(8, 4, 8, 4) : new Thickness(8, 8, 0, 24);
        foreach (var (tab, _) in _symbolTabs)
        {
            tab.HeightRequest = landscape ? 36 : 44;
        }
    }

    private async void OnToolSegmentPencilTapped(object? sender, TappedEventArgs e) =>
        await SelectToolSegmentAsync(AnnotationTool.Pencil);

    private async void OnToolSegmentEraserTapped(object? sender, TappedEventArgs e) =>
        await SelectToolSegmentAsync(AnnotationTool.Eraser);

    private async void OnToolSegmentSymbolsTapped(object? sender, TappedEventArgs e) =>
        await SelectToolSegmentAsync(AnnotationTool.MusicIcons);

    private async Task SelectToolSegmentAsync(AnnotationTool tool)
    {
        if (tool == AnnotationTool.MusicIcons)
        {
            await EnsureBravuraTypefaceLoadedAsync();
        }

        _viewModel.ActiveTool = tool;
        UpdateToolSections();
    }

    private readonly List<(Border Tab, Label Label)> _symbolTabs = new();
    private int _symbolCategoryIndex;

    // One chip per catalog category; the selected one decides which icons
    // the grid shows. Keys are the category's catalog position.
    private void BuildSymbolCategoryTabs()
    {
        var categories = MusicIconCatalog.Categories;
        for (var i = 0; i < categories.Count; i++)
        {
            var index = i;
            var label = new Label
            {
                Text = categories[i].Name,
                FontSize = 13,
                FontFamily = "OpenSansSemibold",
                VerticalOptions = LayoutOptions.Center
            };
            var tab = new Border
            {
                AutomationId = $"SymbolCategoryTab_{index}",
                HeightRequest = _toolSheetLandscape == true ? 36 : 44,
                Padding = new Thickness(16, 0),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(22) },
                Content = label
            };
            tab.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectSymbolCategory(index)) });
            _symbolTabs.Add((tab, label));
            SymbolCategoryTabs.Add(tab);
        }

        SelectSymbolCategory(0);
    }

    private void SelectSymbolCategory(int index)
    {
        _symbolCategoryIndex = index;
        var res = Application.Current!.Resources;
        for (var i = 0; i < _symbolTabs.Count; i++)
        {
            var (tab, label) = _symbolTabs[i];
            var selected = i == index;
            tab.SetAppThemeColor(VisualElement.BackgroundColorProperty,
                (Color)res[selected ? "PrimaryContainerLight" : "SurfaceLight"],
                (Color)res[selected ? "PrimaryContainerDark" : "SurfaceDark"]);
            tab.SetAppThemeColor(Border.StrokeProperty,
                selected ? Colors.Transparent : (Color)res["OutlineLight"],
                selected ? Colors.Transparent : (Color)res["OutlineDark"]);
            label.SetAppThemeColor(Label.TextColorProperty,
                (Color)res[selected ? "OnPrimaryContainerLight" : "TextSecondaryLight"],
                (Color)res[selected ? "OnPrimaryContainerDark" : "TextSecondaryDark"]);
        }

        BindableLayout.SetItemsSource(SymbolGrid, MusicIconCatalog.Categories[index].Icons);
        ApplyToolSheetHeight();
    }

    // Stays set from entering edit mode until Done, Back with nothing left to
    // undo, or a page/mode change: IsEditing alone can drop transiently (undoing
    // a placed symbol clears the selection while the sheet is hidden).
    private bool _inEditSession;

    // True while any annotation editing is going on: a tool sheet is open,
    // a drawing tool is armed, a continuous-mode page is open in the
    // editor, or an icon is selected. The floating bar then shows the
    // editing controls (undo/redo/done) instead of the reading ones.
    private bool IsEditing =>
        ToolSheet.IsVisible
        || _viewModel.IsDrawingToolActive
        || _viewModel.IsContinuousEditing
        || _viewModel.SelectedAnnotation is not null;

    private void UpdateEditingUi()
    {
        var editing = IsEditing;
        if (editing && !_inEditSession)
        {
            _inEditSession = true;
            _viewModel.BeginEditSession();
        }

        EditingBarContent.IsVisible = editing;
        ReadingBarContent.IsVisible = !editing;

        if (editing)
        {
            // The bar must stay reachable (it holds "Listo").
            if (!_isToolbarVisible || !FloatingBar.IsVisible)
            {
                SetToolbarVisible(true, animate: false);
            }

            EditorPageLabel.Text = string.Format(AppStrings.EditorPageFormat, _viewModel.CurrentPageDisplay);
            SetBarButton(UndoButton, UndoGlyph, _viewModel.CanUndo);
            SetBarButton(RedoButton, RedoGlyph, _viewModel.CanRedo);
        }

        // The sheet covers the bottom of the screen: the page pill and the
        // tool FAB would sit on top of it.
        var showBottomChrome = _isToolbarVisible && !ToolSheet.IsVisible;
        // The pill is also hidden while an annotation is selected: it would
        // cover the resize handle of a selection near the bottom edge.
        PageIndicator.IsVisible = showBottomChrome && _viewModel.SelectedAnnotation is null;
        MainToolFab.IsVisible = showBottomChrome;
        if (ToolSheet.IsVisible)
        {
            SetToolFabExpanded(false);
        }

        RepositionSelectionOverlay();
    }

    private static void SetBarButton(ImageButton button, FontImageSource glyph, bool enabled)
    {
        button.IsEnabled = enabled;
        var res = Application.Current!.Resources;
        glyph.SetAppThemeColor(FontImageSource.ColorProperty,
            (Color)res[enabled ? "TextPrimaryLight" : "TextMutedLight"],
            (Color)res[enabled ? "TextPrimaryDark" : "TextMutedDark"]);
    }

    private async void OnEditorDoneTapped(object? sender, TappedEventArgs e) =>
        await ExitEditModeAsync();

    private async Task ExitEditModeAsync()
    {
        try
        {
            await ExitEditModeCoreAsync();
        }
        finally
        {
            // Closing the tools re-runs UpdateEditingUi while the last one is
            // still armed, which would otherwise start a new session.
            _inEditSession = false;
        }
    }

    private async Task ExitEditModeCoreAsync()
    {
        ToolSheet.IsVisible = false;
        SetToolFabExpanded(false);
        _viewModel.SelectedAnnotation = null;

        if (_viewModel.IsContinuousEditing)
        {
            await ExitContinuousEditAsync();
            return;
        }

        _viewModel.ActiveTool = AnnotationTool.MusicIcons;
        UpdateToolSections();
        UpdateSelectionOverlay();
    }

    private void OnEraserRadiusChanged(object? sender, ValueChangedEventArgs e)
    {
        UpdateEraserDrawingViewLineWidth();
        UpdateToolValueLabels();
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
        }
    }

    private void OnPencilWidthChanged(object? sender, ValueChangedEventArgs e)
    {
        UpdatePencilDrawingViewLineWidth();
        UpdateToolValueLabels();
    }

    private void UpdatePencilDrawingViewLineWidth()
    {
        if (PageContainer.Width > 0)
        {
            PencilDrawingView.LineWidth = (float)(_viewModel.PencilStrokeWidth * EditorPageRect().Width);
        }
    }

    // Closes on the initial touch-down, not when the finger lifts, so the
    // panel gets out of the way as soon as the user starts drawing instead
    // of staying open over the page for the whole stroke.
    private void OnPencilDrawingLineStarted(object? sender, DrawingLineStartedEventArgs e)
    {
        _discardPencilLine = false;
        ToolSheet.IsVisible = false;
    }

    // Set when a second finger turns the stroke into a pinch (Android, see
    // AttachAndroidPencilPinchListener); that stroke is not saved.
    private bool _discardPencilLine;

    private async void OnPencilDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        if (_discardPencilLine)
        {
            _discardPencilLine = false;
            return;
        }

        if (PageContainer.Width <= 0 || PageContainer.Height <= 0 || !int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        var linePoints = e.LastDrawingLine.Points;
        if (linePoints.Count < 2)
        {
            return;
        }

        var pageRect = EditorPageRect();
        var normalizedPoints = linePoints
            .Select(p => new StrokePoint((p.X - pageRect.Left) / pageRect.Width, (p.Y - pageRect.Top) / pageRect.Height))
            .ToList();

        await SavePencilStrokeAsync(sheetId, normalizedPoints);
    }

    // Shared by the Pencil tool and direct stylus drawing: one place that
    // saves a stroke with the current pencil color/width (and records undo).
    private async Task SavePencilStrokeAsync(int sheetId, IReadOnlyList<StrokePoint> normalizedPoints)
    {
        await _viewModel.AddClippedStrokeAsync(sheetId, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, normalizedPoints);
        InvalidateAnnotations();
    }

    // Direct stylus drawing (no tool active). The stroke is kept in page-
    // normalized points and painted by the annotation canvases on top of the
    // saved annotations until it has been saved.
    private List<StrokePoint> _stylusLiveStroke = new();
    private int _stylusSheetId;
    private int _stylusPageIndex;
    private bool _stylusInvalidatePending;

    private void OnStylusDrawBegin(float x, float y)
    {
        _stylusLiveStroke = new List<StrokePoint>();
        _stylusPageIndex = _viewModel.CurrentPageIndex;
        _ = int.TryParse(SheetId, out _stylusSheetId);
        AddStylusPoint(x, y);
    }

    // At most one repaint per frame, however many moves arrive.
    private void OnStylusDrawMove(float x, float y)
    {
        AddStylusPoint(x, y);
        if (_stylusInvalidatePending)
        {
            return;
        }

        _stylusInvalidatePending = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
        {
            _stylusInvalidatePending = false;
            InvalidateAnnotations();
        });
    }

    private void AddStylusPoint(float x, float y)
    {
        var pageRect = EditorPageRect();
        if (pageRect.Width <= 0 || pageRect.Height <= 0)
        {
            return;
        }

        var point = new StrokePoint((x - pageRect.Left) / pageRect.Width, (y - pageRect.Top) / pageRect.Height);
        if (_stylusLiveStroke.Count > 0 && _stylusLiveStroke[^1] == point)
        {
            return;
        }

        _stylusLiveStroke.Add(point);
    }

    private void OnStylusDrawCancel()
    {
        _stylusLiveStroke = new List<StrokePoint>();
        InvalidateAnnotations();
    }

    private async void OnStylusDrawEnd()
    {
        var stroke = _stylusLiveStroke;
        var sheetId = _stylusSheetId;
        var pageIndex = _stylusPageIndex;
        try
        {
            // The page may have turned (pedal) since the stroke began.
            if (stroke.Count >= 2 && sheetId > 0 && pageIndex == _viewModel.CurrentPageIndex)
            {
                await SavePencilStrokeAsync(sheetId, stroke);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stylus stroke save failed: {ex}");
        }
        finally
        {
            // A newer stroke may already have started; leave it alone.
            if (ReferenceEquals(stroke, _stylusLiveStroke))
            {
                _stylusLiveStroke = new List<StrokePoint>();
            }

            InvalidateAnnotations();
        }
    }

    private void OnStylusEraseBegin() => _viewModel.BeginEraseBatch();

    private void OnStylusEraseEnd()
    {
        _viewModel.EndEraseBatch();
        EraserRadiusIndicator.IsVisible = false;
    }

    private void DrawLiveStylusStroke(SKCanvas canvas, float width, float height)
    {
        if (_stylusLiveStroke.Count >= 2)
        {
            _annotationPainter.DrawLiveStroke(
                canvas, width, height, _stylusLiveStroke, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, EditorPageFrame());
        }
    }

    private async Task EnsureBravuraTypefaceLoadedAsync()
    {
        if (_annotationPainter.Typeface is not null)
        {
            return;
        }

        using var stream = await FileSystem.OpenAppPackageFileAsync("Bravura.otf");
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        _annotationPainter.Typeface = SKTypeface.FromStream(memoryStream);
    }

    private async void OnIconPickerTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string iconKey)
        {
            _viewModel.VisibleArea = EditorVisibleArea();
            await _viewModel.PlaceIconCommand.ExecuteAsync(iconKey);
            ToolSheet.IsVisible = false;
            UpdateSelectionOverlay();
        }
    }

    // Tiles are recycled when the category changes: repaint with the new glyph.
    private void OnIconGlyphBindingContextChanged(object? sender, EventArgs e) =>
        (sender as SKCanvasView)?.InvalidateSurface();

    private void OnIconGlyphPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (sender is not SKCanvasView { BindingContext: MusicIcon icon })
        {
            return;
        }

        var info = e.Info;
        _annotationPainter.DrawGlyphFitted(canvas, icon.Codepoint, new SKRect(0, 0, info.Width, info.Height), icon.VisualScale);
    }

    private void OnAnnotationCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (AnnotationDetailCanvas.IsVisible && _annotationDetailPainted)
        {
            return;
        }

        _annotationPainter.DrawAnnotations(canvas, e.Info, _viewModel.CurrentPageAnnotations, EditorPageFrame(), _viewModel.CurrentPageCrop);
        DrawLiveStylusStroke(canvas, e.Info.Width, e.Info.Height);
    }

    // Draws the part of the page area in _annotationDetailArea: the same
    // drawing as the base canvas, at k pixels per unit and shifted so the
    // area's corner is the origin.
    private void OnAnnotationDetailPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var area = _annotationDetailArea;
        if (area.IsEmpty || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return;
        }

        var k = e.Info.Width / area.Width;
        canvas.Translate((float)(-area.X * k), (float)(-area.Y * k));
        _annotationPainter.DrawAnnotations(
            canvas,
            (float)(PageContainer.Width * k),
            (float)(PageContainer.Height * k),
            _viewModel.CurrentPageAnnotations,
            EditorPageFrame(),
            _viewModel.CurrentPageCrop);
        DrawLiveStylusStroke(canvas, (float)(PageContainer.Width * k), (float)(PageContainer.Height * k));

        MarkAnnotationDetailPainted();
    }

    // The base canvas keeps drawing until the overlay has really painted, so
    // the annotations never go missing for a frame while it appears.
    private void MarkAnnotationDetailPainted()
    {
        if (!_annotationDetailPainted)
        {
            _annotationDetailPainted = true;
            AnnotationCanvas.InvalidateSurface();
        }
    }
}
