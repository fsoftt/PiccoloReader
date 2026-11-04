using System.Globalization;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Services;

namespace PiccoloReader.Views;

public enum CoachGesture
{
    None,

    /// <summary>Press and hold, then move down a little (drag to reorder).</summary>
    LongPressDrag,

    /// <summary>A single tap.</summary>
    Tap
}

public enum CoachMarkResult
{
    /// <summary>Nothing was shown (no step had a usable target).</summary>
    NotShown,

    Finished,

    Skipped,

    /// <summary>Dismissed from code (the page went away); the user did not finish.</summary>
    Cancelled
}

public sealed class CoachMarkStep
{
    public required string Text { get; init; }

    /// <summary>Bounds of the highlighted element in the overlay coordinate space; null for no cutout.</summary>
    public Func<Rect?>? Target { get; init; }

    /// <summary>
    /// Where the callout and the gesture hint go, given the target. Defaults to the target
    /// itself; a tall target (a tap zone) passes a small rectangle inside it.
    /// </summary>
    public Func<Rect, Rect>? Anchor { get; init; }

    public CoachGesture Gesture { get; init; }

    /// <summary>Shows the drag_indicator icon next to the text.</summary>
    public bool ShowDragIcon { get; init; }

    public double CornerRadius { get; init; } = 14;

    /// <summary>Extra room around the target inside the cutout.</summary>
    public double Padding { get; init; } = 4;

    /// <summary>Drop the step when its target is missing or off screen.</summary>
    public bool RequireTarget { get; init; }
}

/// <summary>
/// Reusable coach mark layer: dims the page, cuts a rounded hole around a target, shows a
/// callout (text, "1/3", Next / Skip) and an optional looping gesture hint. It sits on top of
/// the page content (last child of a root Grid) and swallows all touches while visible.
/// </summary>
public partial class CoachMarkOverlay : ContentView
{
    private const double HandSize = 44;
    private const double RippleSize = 56;
    private const double MaxBubbleWidth = 360;
    private const double ScreenMargin = 16;

    private readonly DimDrawable _dim = new();
    private List<CoachMarkStep> _steps = new();
    private int _index;
    private TaskCompletionSource<CoachMarkResult>? _completion;
    private CancellationTokenSource? _gestureCts;
    private bool _finishing;
    private bool _relayoutQueued;
    private bool _touchWired;

    public CoachMarkOverlay()
    {
        InitializeComponent();
        DimView.Drawable = _dim;
        SkipButton.Text = AppStrings.CoachSkip;
        SemanticProperties.SetDescription(SkipButton, AppStrings.CoachSkip);
        AbsoluteLayout.SetLayoutFlags(Bubble, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutFlags(Hand, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutFlags(Ripple, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        SizeChanged += OnOverlaySizeChanged;
        HandlerChanged += OnOverlayHandlerChanged;
    }

    /// <summary>True from <see cref="ShowAsync"/> until the fade-out has finished.</summary>
    public bool IsShowing => _completion is not null;

    /// <summary>
    /// Shows the steps in order and completes when the user finishes or skips them.
    /// Steps flagged RequireTarget whose target is not on screen are dropped first.
    /// </summary>
    public async Task<CoachMarkResult> ShowAsync(IReadOnlyList<CoachMarkStep> steps)
    {
        if (_completion is not null || steps.Count == 0)
        {
            return CoachMarkResult.NotShown;
        }

        var completion = new TaskCompletionSource<CoachMarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        _finishing = false;
        Opacity = 0;
        IsVisible = true;

        // Hidden views are not laid out: wait until this one has a size on screen.
        for (var i = 0; i < 40 && !IsLaidOut(); i++)
        {
            await Task.Delay(25);
        }

        if (completion.Task.IsCompleted)
        {
            return await completion.Task;
        }

        _steps = steps.Where(step => !step.RequireTarget || IsOnScreen(step.Target?.Invoke())).ToList();
        if (_steps.Count == 0 || !IsLaidOut())
        {
            Hide(CoachMarkResult.NotShown);
            return CoachMarkResult.NotShown;
        }

        _index = 0;
        ShowStep();
        await this.FadeToAsync(1, 220, Easing.CubicOut);
        return await completion.Task;
    }

    /// <summary>Moves to the next step, or finishes after the last one (pedal, Next button).</summary>
    public void Advance()
    {
        if (_completion is null || _finishing || _steps.Count == 0)
        {
            return;
        }

        if (_index >= _steps.Count - 1)
        {
            _ = FinishAsync(CoachMarkResult.Finished);
            return;
        }

        _index++;
        ShowStep();
    }

    /// <summary>Skips the rest of the steps (Skip button, back button).</summary>
    public void Skip()
    {
        if (_completion is not null && !_finishing)
        {
            _ = FinishAsync(CoachMarkResult.Skipped);
        }
    }

    /// <summary>Hides the overlay at once without the user having finished (page left).</summary>
    public void Cancel()
    {
        if (_completion is not null)
        {
            Hide(CoachMarkResult.Cancelled);
        }
    }

    private bool IsLaidOut() => Width > 0 && Height > 0 && Handler is not null;

    private bool IsOnScreen(Rect? rect) =>
        rect is { Width: > 0, Height: > 0 } r
        && r.Left >= -2 && r.Top >= -2 && r.Right <= Width + 2 && r.Bottom <= Height + 2;

    private void ShowStep()
    {
        var step = _steps[_index];
        StopGesture();

        var target = step.Target?.Invoke();
        Rect? cutout = null;
        if (target is { Width: > 0, Height: > 0 } t)
        {
            cutout = t.Inflate(step.Padding, step.Padding);
        }
        else
        {
            target = null;
        }

        _dim.Cutout = cutout;
        _dim.CornerRadius = step.CornerRadius;
        DimView.Invalidate();

        var last = _index >= _steps.Count - 1;
        TextLabel.Text = step.Text;
        BubbleIcon.IsVisible = step.ShowDragIcon;
        StepLabel.Text = string.Format(CultureInfo.CurrentUICulture, AppStrings.CoachStepFormat, _index + 1, _steps.Count);
        NextButton.Text = last ? AppStrings.CoachGotIt : AppStrings.CoachNext;
        SkipButton.IsVisible = !last;
        SemanticProperties.SetDescription(NextButton, NextButton.Text);

        Rect? anchor = target is { } tr ? (step.Anchor?.Invoke(tr) ?? tr) : null;
        LayoutBubble(cutout, anchor, step.Gesture);
        PlaceGesture(step.Gesture, anchor);

        Bubble.Opacity = 0;
        _ = Bubble.FadeToAsync(1, 160);
        SemanticScreenReader.Announce(step.Text);
        StartGesture(step.Gesture);
    }

    // Distance from the fingertip (the anchor centre) down to the lowest point of the hand
    // while it plays: its glyph hangs ~36 below, and the drag moves it another 30.
    private const double HandReachBelow = 36;
    private const double DragTravel = 30;

    private void LayoutBubble(Rect? cutout, Rect? anchor, CoachGesture gesture)
    {
        var width = Math.Min(MaxBubbleWidth, Math.Max(0, Width - 2 * ScreenMargin));
        var insets = ViewBounds.SystemInsets(this);
        CoachRect? target = cutout is { } c ? new CoachRect(c.X, c.Y, c.Width, c.Height) : null;

        // A tall target (a tap zone): the bubble goes beside it, in the dark area.
        if (target is { } zone)
        {
            var side = CoachMarkPlacement.PlaceBubbleBeside(
                Width, Height, zone, width, w => MeasureBubbleHeight(w), insets, ScreenMargin);
            if (side is { } s)
            {
                Bubble.WidthRequest = s.Width;
                AbsoluteLayout.SetLayoutBounds(Bubble, new Rect(s.X, s.Y, s.Width, MeasureBubbleHeight(s.Width)));
                return;
            }
        }

        // Leave room under the target for the hand of the gesture hint to stay visible.
        var reserve = 0.0;
        if (target is { } t && anchor is { } a && gesture != CoachGesture.None)
        {
            var reach = HandReachBelow + (gesture == CoachGesture.LongPressDrag ? DragTravel : 0);
            reserve = Math.Max(0, a.Y + a.Height / 2 + reach - t.Bottom - 12);
        }

        Bubble.WidthRequest = width;
        var height = MeasureBubbleHeight(width);
        var placement = CoachMarkPlacement.PlaceBubble(
            Width, Height, target, width, height, insets, ScreenMargin, gestureReserve: reserve);
        AbsoluteLayout.SetLayoutBounds(Bubble, new Rect(placement.X, placement.Y, width, height));
    }

    private double MeasureBubbleHeight(double width)
    {
        Bubble.WidthRequest = width;
        return Bubble.Measure(width, double.PositiveInfinity).Height;
    }

    private void PlaceGesture(CoachGesture gesture, Rect? anchor)
    {
        if (gesture == CoachGesture.None || anchor is not { } a)
        {
            Hand.IsVisible = false;
            Ripple.IsVisible = false;
            return;
        }

        var cx = a.X + a.Width / 2;
        var cy = a.Y + a.Height / 2;
        Hand.IsVisible = true;
        Ripple.IsVisible = true;
        // The fingertip of the touch_app glyph is up and slightly left of its centre.
        AbsoluteLayout.SetLayoutBounds(Hand, new Rect(cx - HandSize / 2 + 6, cy - HandSize / 2 + 14, HandSize, HandSize));
        AbsoluteLayout.SetLayoutBounds(Ripple, new Rect(cx - RippleSize / 2, cy - RippleSize / 2, RippleSize, RippleSize));
    }

    private void StartGesture(CoachGesture gesture)
    {
        if (gesture == CoachGesture.None)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _gestureCts = cts;
        _ = RunGestureAsync(gesture, cts.Token);
    }

    private void StopGesture()
    {
        _gestureCts?.Cancel();
        _gestureCts = null;
        Hand.CancelAnimations();
        Ripple.CancelAnimations();
        Hand.Opacity = 0;
        Ripple.Opacity = 0;
        Hand.TranslationY = 0;
        Ripple.TranslationY = 0;
        Hand.Scale = 1;
        Ripple.Scale = 0.4;
    }

    // Loops until cancelled: the finger appears, presses (hand shrinks, ripple grows), holds,
    // then for a drag moves down, releases and fades out. Runs on the UI thread through
    // awaited animations and delays, never blocking it.
    private async Task RunGestureAsync(CoachGesture gesture, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Hand.TranslationY = 0;
                Ripple.TranslationY = 0;
                Hand.Scale = 1;
                Ripple.Scale = 0.4;
                Ripple.Opacity = 0;

                await Hand.FadeToAsync(1, 180);
                ct.ThrowIfCancellationRequested();

                // Press.
                await Task.WhenAll(
                    Hand.ScaleToAsync(0.82, 160, Easing.CubicOut),
                    Ripple.ScaleToAsync(1, 350, Easing.CubicOut),
                    Ripple.FadeToAsync(1, 250));
                ct.ThrowIfCancellationRequested();

                if (gesture == CoachGesture.LongPressDrag)
                {
                    // Hold, then drag down.
                    await Task.Delay(550, ct);
                    await Task.WhenAll(
                        Hand.TranslateToAsync(0, 30, 650, Easing.CubicInOut),
                        Ripple.TranslateToAsync(0, 30, 650, Easing.CubicInOut));
                    await Task.Delay(250, ct);
                }
                else
                {
                    await Task.Delay(150, ct);
                }

                // Release.
                await Task.WhenAll(
                    Hand.ScaleToAsync(1, 140),
                    Ripple.FadeToAsync(0, 250),
                    Hand.FadeToAsync(0, 300));
                await Task.Delay(gesture == CoachGesture.LongPressDrag ? 600 : 800, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Step changed or overlay closed.
        }
    }

    private async Task FinishAsync(CoachMarkResult result)
    {
        if (_finishing)
        {
            return;
        }

        _finishing = true;
        StopGesture();
        await this.FadeToAsync(0, 180, Easing.CubicIn);
        Hide(result);
    }

    private void Hide(CoachMarkResult result)
    {
        StopGesture();
        this.CancelAnimations();
        IsVisible = false;
        Opacity = 0;
        var completion = _completion;
        _completion = null;
        _finishing = false;
        completion?.TrySetResult(result);
    }

    private void OnNextClicked(object? sender, EventArgs e) => Advance();

    private void OnSkipClicked(object? sender, EventArgs e) => Skip();

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        // Swallowed: the page below must not react while the guide is up.
    }

    private void OnOverlaySizeChanged(object? sender, EventArgs e)
    {
        // Rotation or resize: targets moved, redo the current step.
        if (_completion is null || _finishing || _steps.Count == 0 || _relayoutQueued || !IsLaidOut())
        {
            return;
        }

        _relayoutQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () =>
        {
            _relayoutQueued = false;
            if (_completion is not null && !_finishing && _index < _steps.Count)
            {
                ShowStep();
            }
        });
    }

    private void OnOverlayHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (!_touchWired && Handler?.PlatformView is Android.Views.View view)
        {
            // Own every touch that no child (the buttons) handled, so nothing reaches the page below.
            _touchWired = true;
            view.Clickable = true;
            view.Touch += (_, args) => args.Handled = true;
        }
#endif
    }

    private sealed class DimDrawable : IDrawable
    {
        private static readonly Color DimColor = Color.FromRgba(0, 0, 0, 0.78f);

        public Rect? Cutout { get; set; }

        public double CornerRadius { get; set; } = 14;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var path = new PathF();
            path.AppendRectangle(dirtyRect);
            if (Cutout is { } hole)
            {
                path.AppendRoundedRectangle((float)hole.X, (float)hole.Y, (float)hole.Width, (float)hole.Height, (float)CornerRadius);
            }

            canvas.FillColor = DimColor;
            canvas.FillPath(path, WindingMode.EvenOdd);

            if (Cutout is { } outline)
            {
                canvas.StrokeColor = Colors.White.WithAlpha(0.9f);
                canvas.StrokeSize = 2;
                canvas.DrawRoundedRectangle((float)outline.X, (float)outline.Y, (float)outline.Width, (float)outline.Height, (float)CornerRadius);
            }
        }
    }
}
