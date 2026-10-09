using Android.Content;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Pinch-zoom and horizontal pan for the continuous reading list. Added to
// the list's RecyclerView with AddOnItemTouchListener, so it sees every
// touch event before the RecyclerView scrolls or a page item handles a
// tap:
// - Two fingers: the listener takes over the gesture (RecyclerView stops
//   scrolling) and reports pinch updates, like PageContainerTouchListener.
// - One finger: never taken over - the RecyclerView keeps doing vertical
//   scrolling (and items keep their taps) - but horizontal movement is
//   reported alongside, so a zoomed-in list pans sideways and diagonally.
//
// Deltas and spans use the same StableX/StableY correction as
// PageContainerTouchListener (see there): GetX/GetY are corrected for the
// RecyclerView's own Scale/TranslationX, which this gesture changes every
// frame.
public sealed class ContinuousZoomTouchListener : Java.Lang.Object, RecyclerView.IOnItemTouchListener
{
    private readonly Action _onPinchStart;
    private readonly Action<double, double, double> _onPinchUpdate;
    private readonly Action _onPinchEnd;
    private readonly Action<double> _onHorizontalPan;
    private readonly float _density;

    private bool _isPinching;
    private double _previousSpan;
    private float _lastPanX;
    private bool _hasLastPanX;

    public ContinuousZoomTouchListener(
        Context context,
        Action onPinchStart,
        Action<double, double, double> onPinchUpdate,
        Action onPinchEnd,
        Action<double> onHorizontalPan)
    {
        _onPinchStart = onPinchStart;
        _onPinchUpdate = onPinchUpdate;
        _onPinchEnd = onPinchEnd;
        _onHorizontalPan = onHorizontalPan;
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

    public bool OnInterceptTouchEvent(RecyclerView rv, MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _isPinching = false;
                _lastPanX = StableX(e, 0, rv);
                _hasLastPanX = true;
                return false;

            case MotionEventActions.PointerDown when e.PointerCount == 2:
                _isPinching = true;
                _hasLastPanX = false;
                _previousSpan = Span(e, rv);
                rv.Parent?.RequestDisallowInterceptTouchEvent(true);
                _onPinchStart();
                return true;

            case MotionEventActions.Move when e.PointerCount == 1 && _hasLastPanX:
                var x = StableX(e, 0, rv);
                _onHorizontalPan((x - _lastPanX) / _density);
                _lastPanX = x;
                return false;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _hasLastPanX = false;
                return false;
        }

        return _isPinching;
    }

    public void OnTouchEvent(RecyclerView rv, MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Move when _isPinching && e.PointerCount >= 2:
                var currentSpan = Span(e, rv);
                if (_previousSpan > 0 && rv.Width > 0 && rv.Height > 0)
                {
                    var focusXFraction = (e.GetX(0) + e.GetX(1)) / 2 / rv.Width;
                    var focusYFraction = (e.GetY(0) + e.GetY(1)) / 2 / rv.Height;
                    _onPinchUpdate(currentSpan / _previousSpan, focusXFraction, focusYFraction);
                }

                _previousSpan = currentSpan;
                break;

            // Lifting one of the two fingers ends the pinch; the remaining
            // finger does nothing until it's lifted too (same as the
            // single-page viewer).
            case MotionEventActions.PointerUp when _isPinching && e.PointerCount == 2:
            case MotionEventActions.Up when _isPinching:
            case MotionEventActions.Cancel when _isPinching:
                _isPinching = false;
                _onPinchEnd();
                break;
        }
    }

    public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
    }
}
