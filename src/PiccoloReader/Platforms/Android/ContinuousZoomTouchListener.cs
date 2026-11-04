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
//   The midpoint of the two fingers pans along with the pinch, and when
//   one finger lifts the other keeps panning (both axes) until it lifts
//   too - the listener holds the gesture until all fingers are up.
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
    private readonly Action<double, double, double, double, double> _onPinchUpdate;
    private readonly Action _onPinchEnd;
    private readonly Action<double> _onHorizontalPan;
    private readonly Action<double, double> _onPinchPan;
    private readonly float _density;
    private readonly float _touchSlop;

    private bool _isPinching;
    private float _downX;
    private float _downY;
    private double _previousSpan;
    private float _lastPanX;
    private bool _hasLastPanX;

    // Pinch pointers are tracked by id so a third finger can't swap them.
    private int _pinchIdA;
    private int _pinchIdB;
    private float _previousMidX;
    private float _previousMidY;

    // One finger left after a pinch: pans both axes via _onPinchPan.
    private bool _isPanAfterPinch;
    private int _panPointerId;
    private float _panAfterPinchX;
    private float _panAfterPinchY;

    public ContinuousZoomTouchListener(
        Context context,
        Action onPinchStart,
        Action<double, double, double, double, double> onPinchUpdate,
        Action onPinchEnd,
        Action<double> onHorizontalPan,
        Action<double, double> onPinchPan)
    {
        _onPinchStart = onPinchStart;
        _onPinchUpdate = onPinchUpdate;
        _onPinchEnd = onPinchEnd;
        _onHorizontalPan = onHorizontalPan;
        _onPinchPan = onPinchPan;
        _density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 8 * _density;
    }

    // True while the current gesture is still a candidate tap (one finger,
    // moved less than the touch slop since ACTION_DOWN). The list's MAUI tap
    // recognizer can't tell a pan from a tap here: while the finger drags
    // the zoomed list sideways, the list translates under it, so the finger's
    // position in the list's own coordinates barely changes and the gesture
    // detector still sees a tap on release. Tap handlers consult this (it
    // stays valid after ACTION_UP, until the next ACTION_DOWN).
    public bool IsTap { get; private set; }

    // Stylus gestures (direct drawing, erasing) never scroll or zoom the list.
    public StylusGestureHandler? Stylus { get; set; }

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
    private void StartPinch(RecyclerView rv, MotionEvent e)
    {
        _isPinching = true;
        _isPanAfterPinch = false;
        IsTap = false;
        _hasLastPanX = false;
        _pinchIdA = e.GetPointerId(0);
        _pinchIdB = e.GetPointerId(1);
        _previousSpan = Span(e, rv, 0, 1);
        _previousMidX = (StableX(e, 0, rv) + StableX(e, 1, rv)) / 2;
        _previousMidY = (StableY(e, 0, rv) + StableY(e, 1, rv)) / 2;
        rv.Parent?.RequestDisallowInterceptTouchEvent(true);
        _onPinchStart();
    }

    public bool OnInterceptTouchEvent(RecyclerView rv, MotionEvent e)
    {
        if (e.ActionMasked == MotionEventActions.Down)
        {
            // The tap/pan bookkeeping starts first, so a stylus tap is still a tap.
            var intercepted = InterceptFingerGesture(rv, e);
            return HandleStylus(rv, e) || intercepted;
        }

        return HandleStylus(rv, e) || InterceptFingerGesture(rv, e);
    }

    // A finished stylus gesture must not leave a stale IsTap behind for the
    // next reader (the tap itself is reported before this runs).
    private bool HandleStylus(RecyclerView rv, MotionEvent e)
    {
        var handled = Stylus?.Handle(rv, e) == true;
        if (handled && e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            IsTap = false;
        }

        return handled;
    }

    // Called when a stylus takes over a gesture a palm or finger started.
    public void ResetGesture()
    {
        if (_isPinching)
        {
            _isPinching = false;
            _onPinchEnd();
        }

        _isPanAfterPinch = false;
        _hasLastPanX = false;
        IsTap = false;
    }

    private bool InterceptFingerGesture(RecyclerView rv, MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _isPinching = false;
                _downX = StableX(e, 0, rv);
                _downY = StableY(e, 0, rv);
                IsTap = true;
                _lastPanX = _downX;
                _hasLastPanX = true;
                return false;

            case MotionEventActions.PointerDown when e.PointerCount == 2:
                StartPinch(rv, e);
                return true;

            case MotionEventActions.Move when e.PointerCount == 1 && _hasLastPanX:
                var x = StableX(e, 0, rv);
                if (IsTap && (Math.Abs(x - _downX) > _touchSlop || Math.Abs(StableY(e, 0, rv) - _downY) > _touchSlop))
                {
                    IsTap = false;
                }

                _onHorizontalPan((x - _lastPanX) / _density);
                _lastPanX = x;
                return false;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _hasLastPanX = false;
                return false;
        }

        return _isPinching || _isPanAfterPinch;
    }

    public void OnTouchEvent(RecyclerView rv, MotionEvent e)
    {
        if (HandleStylus(rv, e))
        {
            return;
        }

        HandleFingerTouch(rv, e);
    }

    private void HandleFingerTouch(RecyclerView rv, MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Move when _isPinching && e.PointerCount >= 2:
                var indexA = e.FindPointerIndex(_pinchIdA);
                var indexB = e.FindPointerIndex(_pinchIdB);
                if (indexA < 0 || indexB < 0)
                {
                    break;
                }

                var currentSpan = Span(e, rv, indexA, indexB);
                var midX = (StableX(e, indexA, rv) + StableX(e, indexB, rv)) / 2;
                var midY = (StableY(e, indexA, rv) + StableY(e, indexB, rv)) / 2;
                if (_previousSpan > 0 && rv.Width > 0 && rv.Height > 0)
                {
                    // Focus from the same stable coordinates as the span: the
                    // list point under the midpoint is (mid - Translation) / Scale.
                    var focusXFraction = (_previousMidX - rv.TranslationX) / rv.ScaleX / rv.Width;
                    var focusYFraction = (_previousMidY - rv.TranslationY) / rv.ScaleY / rv.Height;
                    _onPinchUpdate(
                        currentSpan / _previousSpan,
                        focusXFraction,
                        focusYFraction,
                        (midX - _previousMidX) / _density,
                        (midY - _previousMidY) / _density);
                }

                _previousSpan = currentSpan;
                _previousMidX = midX;
                _previousMidY = midY;
                break;

            // A second finger after a pan-after-pinch resumes the pinch.
            case MotionEventActions.PointerDown when _isPanAfterPinch && e.PointerCount == 2:
                StartPinch(rv, e);
                break;

            case MotionEventActions.Move when _isPanAfterPinch:
                var panIndex = e.FindPointerIndex(_panPointerId);
                if (panIndex >= 0)
                {
                    var panX = StableX(e, panIndex, rv);
                    var panY = StableY(e, panIndex, rv);
                    _onPinchPan((panX - _panAfterPinchX) / _density, (panY - _panAfterPinchY) / _density);
                    _panAfterPinchX = panX;
                    _panAfterPinchY = panY;
                }

                break;

            // Lifting one of the two pinch fingers ends the pinch (zoom
            // settle) and the remaining finger keeps panning until it's
            // lifted too. Lifting some other (third) finger changes nothing.
            case MotionEventActions.PointerUp when _isPinching:
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
                        _panAfterPinchX = StableX(e, remainingIndex, rv);
                        _panAfterPinchY = StableY(e, remainingIndex, rv);
                    }
                }

                break;

            // The panning finger lifted while another is still down: hand
            // the pan over to it (baseline reset, so no jump).
            case MotionEventActions.PointerUp when _isPanAfterPinch && e.GetPointerId(e.ActionIndex) == _panPointerId:
                var nextIndex = e.ActionIndex == 0 ? 1 : 0;
                if (nextIndex < e.PointerCount)
                {
                    _panPointerId = e.GetPointerId(nextIndex);
                    _panAfterPinchX = StableX(e, nextIndex, rv);
                    _panAfterPinchY = StableY(e, nextIndex, rv);
                }

                break;

            case MotionEventActions.Up when _isPinching:
            case MotionEventActions.Cancel when _isPinching:
                _isPinching = false;
                _onPinchEnd();
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _isPanAfterPinch = false;
                break;
        }
    }

    public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
    }
}
