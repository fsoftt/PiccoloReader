using Android.Content;
using Android.Views;
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
    private readonly Action<double, double, double> _onPinchUpdate;
    private readonly Action _onPinchEnd;
    private readonly Action<double, double> _onPanUpdate;
    private readonly Action _onPanEnd;
    private readonly float _density;

    private bool _isPossiblePanOrTap;
    private bool _isPinching;
    private float _panStartX;
    private float _panStartY;
    private long _downTimeMs;
    private double _previousSpan;

    public PageContainerTouchListener(
        Context context,
        Action<double, double> onTap,
        Action onPinchStart,
        Action<double, double, double> onPinchUpdate,
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

    private static float StableX(MotionEvent e, int pointerIndex, View v) => e.GetX(pointerIndex) * v.ScaleX + v.TranslationX;

    private static float StableY(MotionEvent e, int pointerIndex, View v) => e.GetY(pointerIndex) * v.ScaleY + v.TranslationY;

    private static double Span(MotionEvent e, View v)
    {
        var dx = StableX(e, 0, v) - StableX(e, 1, v);
        var dy = StableY(e, 0, v) - StableY(e, 1, v);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (v is null || e is null)
        {
            return false;
        }

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _isPossiblePanOrTap = true;
                _isPinching = false;
                _panStartX = StableX(e, 0, v);
                _panStartY = StableY(e, 0, v);
                _downTimeMs = Java.Lang.JavaSystem.CurrentTimeMillis();
                break;

            case MotionEventActions.PointerDown:
                // A second finger arrived - this is a pinch, not a pan/tap.
                if (e.PointerCount == 2)
                {
                    _isPossiblePanOrTap = false;
                    _isPinching = true;
                    _previousSpan = Span(e, v);
                    _onPinchStart();
                }

                break;

            case MotionEventActions.Move:
                if (_isPinching && e.PointerCount >= 2)
                {
                    var currentSpan = Span(e, v);
                    if (_previousSpan > 0 && v.Width > 0 && v.Height > 0)
                    {
                        var rawScaleFactor = currentSpan / _previousSpan;
                        var focusXFraction = (e.GetX(0) + e.GetX(1)) / 2 / v.Width;
                        var focusYFraction = (e.GetY(0) + e.GetY(1)) / 2 / v.Height;
                        _onPinchUpdate(rawScaleFactor, focusXFraction, focusYFraction);
                    }

                    _previousSpan = currentSpan;
                }
                else if (_isPossiblePanOrTap && e.PointerCount == 1)
                {
                    _onPanUpdate((StableX(e, 0, v) - _panStartX) / _density, (StableY(e, 0, v) - _panStartY) / _density);
                }

                break;

            case MotionEventActions.PointerUp:
                // Dropping from 2 fingers to 1 ends the pinch. The
                // remaining finger isn't picked up as a pan candidate
                // (matches the previous behavior) - the gesture just ends
                // here, same as a full release.
                if (_isPinching && e.PointerCount == 2)
                {
                    _isPinching = false;
                    _onPinchEnd();
                }

                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isPinching)
                {
                    _isPinching = false;
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
