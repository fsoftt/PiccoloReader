using Android.Views;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Lets a two-finger pinch zoom the page while a DrawingView (Pencil) covers
// PageContainer and would otherwise take every touch. Set as the
// DrawingView's OnTouchListener, which runs before the view's own
// OnTouchEvent:
// - One finger: returns false, so the DrawingView draws as usual.
// - Second finger goes down: ends the stroke in progress with a synthetic
//   ACTION_UP (onPinchBegin runs first, so the page can discard that stroke
//   instead of saving a short line), then hands the rest of the gesture -
//   starting with this ACTION_POINTER_DOWN - to PageContainer's own touch
//   listener until the last finger lifts.
//
// The DrawingView fills PageContainer at its origin with no transform of its
// own, so its local coordinates match PageContainer's and the events can be
// forwarded unchanged.
public sealed class DrawingPinchTouchListener : Java.Lang.Object, View.IOnTouchListener
{
    private readonly Action _onPinchBegin;
    private readonly Func<(View.IOnTouchListener Listener, View View)?> _pinchTarget;

    private bool _isForwarding;

    // Stylus gestures (eraser tip / side button) are handled before the pinch logic.
    public StylusGestureHandler? Stylus { get; set; }

    public DrawingPinchTouchListener(Action onPinchBegin, Func<(View.IOnTouchListener Listener, View View)?> pinchTarget)
    {
        _onPinchBegin = onPinchBegin;
        _pinchTarget = pinchTarget;
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

        if (e.ActionMasked == MotionEventActions.Down)
        {
            _isForwarding = false;
            return false;
        }

        if (!_isForwarding)
        {
            if (e.ActionMasked != MotionEventActions.PointerDown || e.PointerCount != 2 || _pinchTarget() is null)
            {
                return false;
            }

            _isForwarding = true;
            _onPinchBegin();
            var up = MotionEvent.Obtain(e.DownTime, e.EventTime, MotionEventActions.Up, e.GetX(0), e.GetY(0), e.MetaState);
            if (up is not null)
            {
                v.OnTouchEvent(up);
                up.Recycle();
            }
        }

        if (_pinchTarget() is { } target)
        {
            target.Listener.OnTouch(target.View, e);
        }

        if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            _isForwarding = false;
        }

        return true;
    }
}
