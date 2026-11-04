using Android.Content;
using Android.Views;
using PiccoloReader.Core.Services.PageTurn;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Unified touch handler for the sheet viewer's PageContainer, replacing
// MAUI's separate Tap/Pinch/PanGestureRecognizer stack on Android (see
// SheetViewerPage.AttachAndroidPageContainerTouchListener for why: those
// recognizers race on real hardware when two fingers land even slightly
// out of sync, intermittently swallowing pinch gestures as a one-finger
// pan). This classifies deterministically from Android's own MotionEvent
// pointer count instead: a sequence stays a pan/tap candidate only until a
// second pointer goes down, at which point it becomes a pinch for the rest
// of the gesture.
//
// Pinch span/pan deltas are hand-rolled here instead of using Android's
// own ScaleGestureDetector. The reason: MotionEvent.GetX()/GetY(), as
// delivered to a view's OnTouchListener, are already corrected for that
// view's OWN live transform (Scale/TranslationX/Y) - confirmed empirically
// (GetX == RawX at Scale=1/Translation=0; they diverge by tens of pixels
// once zoomed). That's normally fine (and is exactly what's wanted for a
// one-shot "where in the content did this land" conversion - see
// FinishPanOrTap's tap branch, which deliberately keeps using GetX/GetY
// for that reason). But PageContainer's Scale/TranslationX/Y are being
// updated by THIS SAME gesture, every frame - so a frame-to-frame span or
// delta computed from GetX/GetY is comparing two points that were each
// corrected against a DIFFERENT (and progressively larger) scale, which
// silently shrinks the measured motion as zoom increases. That's the
// mechanism behind two real-device symptoms: needing a bigger pinch
// gesture to get the same zoom change at higher zoom levels, and the
// zoom "shaking" while holding fingers still (residual per-frame touch
// jitter gets relatively amplified as the usable coordinate range keeps
// shrinking under it).
//
// StableX/StableY undo that per-view correction using the exact same
// Scale/TranslationX the platform view just applied (GetX*ScaleX+
// TranslationX), recovering a value anchored to the view's fixed layout
// origin - stable and comparable across frames regardless of how much
// Scale/Translation changed in between. Verified empirically: it
// reproduces RawX (pointer 0, always available unscaled) to within
// floating-point noise. Used for span (pinch) and pan deltas, which are
// frame-to-frame differences; NOT used for the pinch focus point or tap
// hit-testing, which are one-shot screen-to-content conversions that want
// the normal corrected coordinates.
public sealed class PageContainerTouchListener : Java.Lang.Object, View.IOnTouchListener
{
    private const float TapSlopDp = 10f;
    private const long TapMaxDurationMs = 300;

    private readonly Action<double, double> _onTap;
    private readonly Action _onPinchStart;
    private readonly Action<double, double, double, double, double> _onPinchUpdate;
    private readonly Action _onPinchEnd;
    private readonly Action<double, double> _onPanUpdate;
    private readonly Action _onPanEnd;
    private readonly float _density;

    // Stylus gestures are handled first and never reach the pan/pinch/tap logic.
    public StylusGestureHandler? Stylus { get; set; }

    // Long-press on a figure (not pinch/pan/tap): given the container-normalized
    // point, returns true when it handled the press. A handled press swallows
    // the rest of the gesture, so it is never also a tap or a pan.
    public Func<double, double, bool>? OnLongPress { get; set; }

    private LongPressDetector? _longPress;
    private bool _longPressConsumed;

    private bool _isPossiblePanOrTap;
    private bool _isPinching;
    private float _panStartX;
    private float _panStartY;
    private long _downTimeMs;
    private double _previousSpan;

    // The two pointers driving the pinch, tracked by id (not index) so a
    // third finger touching down can't swap which fingers are measured.
    private int _pinchIdA;
    private int _pinchIdB;
    private float _previousMidX;
    private float _previousMidY;

    // One finger left after a pinch: it keeps panning from where the pinch
    // left the page. Never a tap or page turn; releasing just commits the
    // offsets (through _onPinchEnd).
    private bool _isPanAfterPinch;
    private int _panPointerId;

    public PageContainerTouchListener(
        Context context,
        Action<double, double> onTap,
        Action onPinchStart,
        Action<double, double, double, double, double> onPinchUpdate,
        Action onPinchEnd,
        Action<double, double> onPanUpdate,
        Action onPanEnd)
    {
        _onTap = onTap;
        _onPinchStart = onPinchStart;
        _onPinchUpdate = onPinchUpdate;
        _onPinchEnd = onPinchEnd;
        _onPanUpdate = onPanUpdate;
        _onPanEnd = onPanEnd;
        _density = context.Resources?.DisplayMetrics?.Density ?? 1f;
    }

    // Called when a stylus takes over a gesture a palm or finger started:
    // ends the pan or pinch in progress without treating it as a tap.
    public void ResetGesture()
    {
        if (_isPinching)
        {
            _isPinching = false;
            _onPinchEnd();
        }
        else if (_isPanAfterPinch)
        {
            _isPanAfterPinch = false;
            _onPinchEnd();
        }
        else if (_isPossiblePanOrTap)
        {
            _onPanEnd();
        }

        _isPossiblePanOrTap = false;
    }

    private static float StableX(MotionEvent e, int pointerIndex, View v) => e.GetX(pointerIndex) * v.ScaleX + v.TranslationX;

    private static float StableY(MotionEvent e, int pointerIndex, View v) => e.GetY(pointerIndex) * v.ScaleY + v.TranslationY;

    private static double Span(MotionEvent e, View v, int indexA, int indexB)
    {
        var dx = StableX(e, indexA, v) - StableX(e, indexB, v);
        var dy = StableY(e, indexA, v) - StableY(e, indexB, v);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Starts (or resumes, after a pan-after-pinch) a pinch on the first two
    // pointers, with fresh span and midpoint baselines.
    private void StartPinch(MotionEvent e, View v)
    {
        _isPossiblePanOrTap = false;
        _isPanAfterPinch = false;
        _isPinching = true;
        _pinchIdA = e.GetPointerId(0);
        _pinchIdB = e.GetPointerId(1);
        _previousSpan = Span(e, v, 0, 1);
        _previousMidX = (StableX(e, 0, v) + StableX(e, 1, v)) / 2;
        _previousMidY = (StableY(e, 0, v) + StableY(e, 1, v)) / 2;
        _onPinchStart();
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (v is null || e is null)
        {
            return false;
        }

        if (Stylus?.Handle(v, e) == true)
        {
            return true;
        }

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _isPossiblePanOrTap = true;
                _isPinching = false;
                _isPanAfterPinch = false;
                _panStartX = StableX(e, 0, v);
                _panStartY = StableY(e, 0, v);
                _downTimeMs = Java.Lang.JavaSystem.CurrentTimeMillis();
                StartLongPress(v, e);
                break;

            case MotionEventActions.PointerDown:
                // A second finger arrived - this is a pinch, not a pan/tap
                // (also when it follows a pan-after-pinch: pinch resumes).
                _longPress?.Cancel();
                if (_longPressConsumed)
                {
                    break;
                }

                if (e.PointerCount == 2)
                {
                    StartPinch(e, v);
                }

                break;

            case MotionEventActions.Move:
                if (_longPressConsumed)
                {
                    break;
                }

                if (e.PointerCount == 1)
                {
                    _longPress?.Move(e.RawX, e.RawY);
                }

                if (_isPinching && e.PointerCount >= 2)
                {
                    var indexA = e.FindPointerIndex(_pinchIdA);
                    var indexB = e.FindPointerIndex(_pinchIdB);
                    if (indexA < 0 || indexB < 0)
                    {
                        break;
                    }

                    var currentSpan = Span(e, v, indexA, indexB);
                    var midX = (StableX(e, indexA, v) + StableX(e, indexB, v)) / 2;
                    var midY = (StableY(e, indexA, v) + StableY(e, indexB, v)) / 2;
                    if (_previousSpan > 0 && v.Width > 0 && v.Height > 0)
                    {
                        var rawScaleFactor = currentSpan / _previousSpan;

                        // The stable midpoint is a screen position; with
                        // AnchorX/Y = 0 the page point under it is
                        // (mid - Translation) / Scale in the view's local
                        // space. The focus is the point that was under the
                        // fingers last frame (the previous midpoint, against
                        // the unchanged current transform); the midpoint
                        // delta is then added on top, so the zoom anchoring
                        // and the pan stay exact.
                        var focusXFraction = PagedViewport.FocusFraction(_previousMidX, v.TranslationX, v.ScaleX, v.Width);
                        var focusYFraction = PagedViewport.FocusFraction(_previousMidY, v.TranslationY, v.ScaleY, v.Height);

                        // The midpoint's own motion pans the page, in dp like
                        // the one-finger pan deltas.
                        _onPinchUpdate(rawScaleFactor, focusXFraction, focusYFraction, (midX - _previousMidX) / _density, (midY - _previousMidY) / _density);
                    }

                    _previousSpan = currentSpan;
                    _previousMidX = midX;
                    _previousMidY = midY;
                }
                else if (_isPanAfterPinch)
                {
                    var panIndex = e.FindPointerIndex(_panPointerId);
                    if (panIndex >= 0)
                    {
                        _onPanUpdate((StableX(e, panIndex, v) - _panStartX) / _density, (StableY(e, panIndex, v) - _panStartY) / _density);
                    }
                }
                else if (_isPossiblePanOrTap && e.PointerCount == 1)
                {
                    _onPanUpdate((StableX(e, 0, v) - _panStartX) / _density, (StableY(e, 0, v) - _panStartY) / _density);
                }

                break;

            case MotionEventActions.PointerUp:
                // Lifting one of the two pinch fingers ends the pinch and the
                // other one carries on as a pan from where the pinch left the
                // page. Lifting some other (third) finger changes nothing.
                if (_isPinching)
                {
                    var liftedId = e.GetPointerId(e.ActionIndex);
                    if (liftedId == _pinchIdA || liftedId == _pinchIdB)
                    {
                        var remainingId = liftedId == _pinchIdA ? _pinchIdB : _pinchIdA;
                        var remainingIndex = e.FindPointerIndex(remainingId);
                        _isPinching = false;
                        _onPinchEnd();
                        if (remainingIndex >= 0)
                        {
                            _isPanAfterPinch = true;
                            _panPointerId = remainingId;
                            _panStartX = StableX(e, remainingIndex, v);
                            _panStartY = StableY(e, remainingIndex, v);
                        }
                    }
                }
                else if (_isPanAfterPinch && e.GetPointerId(e.ActionIndex) == _panPointerId)
                {
                    // The panning finger lifted while another is still down:
                    // hand the pan over to it. _onPinchEnd re-bases the
                    // offsets on the current translation, so no jump.
                    var nextIndex = e.ActionIndex == 0 ? 1 : 0;
                    if (nextIndex < e.PointerCount)
                    {
                        _onPinchEnd();
                        _panPointerId = e.GetPointerId(nextIndex);
                        _panStartX = StableX(e, nextIndex, v);
                        _panStartY = StableY(e, nextIndex, v);
                    }
                }

                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _longPress?.Cancel();
                if (_longPressConsumed)
                {
                    _longPressConsumed = false;
                    _isPossiblePanOrTap = false;
                    _onPanEnd();
                    break;
                }

                if (_isPinching)
                {
                    _isPinching = false;
                    _onPinchEnd();
                }
                else if (_isPanAfterPinch)
                {
                    // Just commits the offsets - never a tap or page turn.
                    _isPanAfterPinch = false;
                    _onPinchEnd();
                }
                else if (_isPossiblePanOrTap)
                {
                    FinishPanOrTap(v, e);
                }

                _isPossiblePanOrTap = false;
                break;
        }

        return true;
    }

    private void StartLongPress(View v, MotionEvent e)
    {
        _longPressConsumed = false;
        if (OnLongPress is null || v.Width <= 0 || v.Height <= 0)
        {
            return;
        }

        // Same corrected coordinate a tap hit-tests with.
        var normalizedX = e.GetX(0) / v.Width;
        var normalizedY = e.GetY(0) / v.Height;
        _longPress ??= new LongPressDetector(_density);
        _longPress.Start(e.RawX, e.RawY, () =>
        {
            if (_isPossiblePanOrTap && !_isPinching && !_isPanAfterPinch && OnLongPress?.Invoke(normalizedX, normalizedY) == true)
            {
                _longPressConsumed = true;
            }
        });
    }

    private void FinishPanOrTap(View v, MotionEvent e)
    {
        var totalXDp = (StableX(e, 0, v) - _panStartX) / _density;
        var totalYDp = (StableY(e, 0, v) - _panStartY) / _density;
        var travelDp = Math.Sqrt(totalXDp * totalXDp + totalYDp * totalYDp);
        var durationMs = Java.Lang.JavaSystem.CurrentTimeMillis() - _downTimeMs;

        if (travelDp < TapSlopDp && durationMs < TapMaxDurationMs && v.Width > 0 && v.Height > 0)
        {
            // Tap hit-testing wants the normal, transform-corrected
            // coordinate (screen position mapped to content position at
            // this instant) - not the stable one used for pan/span deltas
            // above. See the class comment for why these differ.
            _onTap(e.GetX(0) / v.Width, e.GetY(0) / v.Height);
        }
        else
        {
            _onPanUpdate(totalXDp, totalYDp);
            _onPanEnd();
        }
    }
}
