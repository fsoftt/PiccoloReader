using Android.Views;
using PiccoloReader.Core.Services.Stylus;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Stylus-specific part of a touch listener. Called first by the listener on
// every event; returns true when it consumed the event, so the normal finger
// handling (pan, pinch, tap) never sees a stylus gesture.
//
// The role of a gesture is decided when its pointer goes down (see
// StylusInputClassifier). That is ACTION_DOWN, or ACTION_POINTER_DOWN when a
// palm or finger was already on the screen (the finger gesture is cancelled
// first):
// - Draw: only handled here when canDraw() is true (direct drawing on the
//   page). Otherwise the gesture is left to the caller (the Pencil
//   DrawingView draws it natively). A Draw gesture that ends as a tap (little
//   movement, short) is discarded and reported through onTap instead, so a
//   stylus tap acts like a finger tap.
// - Erase (eraser tip, or stylus side button held): handled here whenever
//   canErase() is true.
// If the side button is pressed in the middle of a stroke, the stroke is
// dropped (own stroke: drawCancel; native stroke: cancelOngoing) and the
// rest of the gesture erases.
// While a gesture is handled here, every other pointer (a resting palm,
// fingers) is ignored, and after it ends with other pointers still down their
// events are swallowed until the next ACTION_DOWN, so the finger listener
// never sees orphan Move/Up events.
//
// Points are reported in dp, in the view's own (transform-corrected) local
// coordinates, which is the PageContainer space the page works in.
public sealed class StylusGestureHandler
{
    private enum Mode
    {
        None,
        Draw,
        Erase
    }

    private readonly Func<bool> _canDraw;
    private readonly Func<bool> _canErase;
    private readonly Action<float, float> _drawBegin;
    private readonly Action<float, float> _drawMove;
    private readonly Action _drawEnd;
    private readonly Action _drawCancel;
    private readonly Action _eraseBegin;
    private readonly Action<float, float> _eraseMove;
    private readonly Action _eraseEnd;
    private readonly Action<double, double>? _onTap;
    private readonly Action<View, MotionEvent>? _cancelOngoing;

    private Mode _mode;
    private int _pointerId;
    private bool _nativeStylusGesture;
    private bool _ignoreUntilDown;
    private View? _view;
    private float _downX;
    private float _downY;
    private long _downTime;
    private long _gestureDownTime;
    private double _maxTravelPx;

    public StylusGestureHandler(
        Func<bool> canDraw,
        Func<bool> canErase,
        Action<float, float> drawBegin,
        Action<float, float> drawMove,
        Action drawEnd,
        Action drawCancel,
        Action eraseBegin,
        Action<float, float> eraseMove,
        Action eraseEnd,
        Action<double, double>? onTap = null,
        Action<View, MotionEvent>? cancelOngoing = null)
    {
        _canDraw = canDraw;
        _canErase = canErase;
        _drawBegin = drawBegin;
        _drawMove = drawMove;
        _drawEnd = drawEnd;
        _drawCancel = drawCancel;
        _eraseBegin = eraseBegin;
        _eraseMove = eraseMove;
        _eraseEnd = eraseEnd;
        _onTap = onTap;
        _cancelOngoing = cancelOngoing;
    }

    public static StylusRole RoleOf(MotionEvent e, int pointerIndex)
    {
        var tool = e.GetToolType(pointerIndex) switch
        {
            MotionEventToolType.Finger => PointerToolKind.Finger,
            MotionEventToolType.Stylus => PointerToolKind.Stylus,
            MotionEventToolType.Eraser => PointerToolKind.Eraser,
            MotionEventToolType.Mouse => PointerToolKind.Mouse,
            _ => PointerToolKind.Unknown
        };

        var buttons = e.ButtonState;
        return StylusInputClassifier.Classify(
            tool,
            buttons.HasFlag(MotionEventButtonState.StylusPrimary),
            buttons.HasFlag(MotionEventButtonState.StylusSecondary));
    }

    // Abandons any gesture in progress (the page is going away).
    public void Cancel() => Abort();

    public bool Handle(View v, MotionEvent e)
    {
        var action = e.ActionMasked;

        if (_mode == Mode.None)
        {
            return HandleIdle(v, e, action);
        }

        // A gesture is being handled here.
        if (action == MotionEventActions.Down)
        {
            // The same DOWN seen twice (intercept, then replayed to the touch
            // handler) is not a new gesture.
            if (e.DownTime == _gestureDownTime)
            {
                return true;
            }

            // Stale state (a missed Up): drop it and start over.
            Abort();
            return Handle(v, e);
        }

        var index = e.FindPointerIndex(_pointerId);
        switch (action)
        {
            case MotionEventActions.Move:
                if (index < 0)
                {
                    break;
                }

                if (_mode == Mode.Draw && _canErase() && RoleOf(e, index) == StylusRole.Erase)
                {
                    _drawCancel();
                    _mode = Mode.Erase;
                    _eraseBegin();
                }

                TrackTravel(e.GetX(index), e.GetY(index));
                if (_mode == Mode.Draw)
                {
                    for (var h = 0; h < e.HistorySize; h++)
                    {
                        _drawMove(Dp(v, e.GetHistoricalX(index, h)), Dp(v, e.GetHistoricalY(index, h)));
                    }

                    _drawMove(Dp(v, e.GetX(index)), Dp(v, e.GetY(index)));
                }
                else
                {
                    _eraseMove(Dp(v, e.GetX(index)), Dp(v, e.GetY(index)));
                }

                break;

            case MotionEventActions.Up:
                if (index >= 0)
                {
                    TrackTravel(e.GetX(index), e.GetY(index));
                }

                Finish(v, e, index, otherPointersDown: false);
                break;

            case MotionEventActions.PointerUp:
                if (e.ActionIndex == index)
                {
                    Finish(v, e, index, otherPointersDown: e.PointerCount > 1);
                }

                break;

            case MotionEventActions.Cancel:
                Abort();
                break;
        }

        return true;
    }

    private bool HandleIdle(View v, MotionEvent e, MotionEventActions action)
    {
        if (_ignoreUntilDown)
        {
            if (action != MotionEventActions.Down)
            {
                if (action is MotionEventActions.Up or MotionEventActions.Cancel)
                {
                    _ignoreUntilDown = false;
                }

                return true;
            }

            _ignoreUntilDown = false;
        }

        if (action == MotionEventActions.Down)
        {
            _nativeStylusGesture = false;
            var role = RoleOf(e, 0);
            if (TryBegin(v, e, 0, role))
            {
                return true;
            }

            // A plain stylus stroke left to the native view: watch it for
            // the side button being pressed later.
            _nativeStylusGesture = role == StylusRole.Draw && _canErase();
            return false;
        }

        if (action == MotionEventActions.PointerDown)
        {
            // A stylus touching down while a palm or finger is already on the
            // screen takes over: that finger gesture is cancelled.
            var index = e.ActionIndex;
            var role = RoleOf(e, index);
            if (role != StylusRole.Finger && CanHandle(role))
            {
                _cancelOngoing?.Invoke(v, e);
                _nativeStylusGesture = false;
                TryBegin(v, e, index, role);
                return true;
            }

            return false;
        }

        if (_nativeStylusGesture && action == MotionEventActions.Move && e.PointerCount == 1 && RoleOf(e, 0) == StylusRole.Erase)
        {
            _nativeStylusGesture = false;
            _cancelOngoing?.Invoke(v, e);
            Begin(v, e, 0, Mode.Erase);
            return true;
        }

        if (action is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            _nativeStylusGesture = false;
        }

        return false;
    }

    private bool CanHandle(StylusRole role) =>
        role == StylusRole.Draw ? _canDraw() : role == StylusRole.Erase && _canErase();

    private bool TryBegin(View v, MotionEvent e, int index, StylusRole role)
    {
        if (!CanHandle(role))
        {
            return false;
        }

        Begin(v, e, index, role == StylusRole.Draw ? Mode.Draw : Mode.Erase);
        return true;
    }

    private void Begin(View v, MotionEvent e, int index, Mode mode)
    {
        _mode = mode;
        _view = v;
        _pointerId = e.GetPointerId(index);
        _downX = e.GetX(index);
        _downY = e.GetY(index);
        _downTime = e.EventTime;
        _gestureDownTime = e.DownTime;
        _maxTravelPx = 0;
        v.Parent?.RequestDisallowInterceptTouchEvent(true);
        var x = Dp(v, _downX);
        var y = Dp(v, _downY);
        if (mode == Mode.Draw)
        {
            _drawBegin(x, y);
        }
        else
        {
            _eraseBegin();
            _eraseMove(x, y);
        }
    }

    private void TrackTravel(float x, float y)
    {
        var dx = x - _downX;
        var dy = y - _downY;
        _maxTravelPx = Math.Max(_maxTravelPx, Math.Sqrt(dx * dx + dy * dy));
    }

    private void Finish(View v, MotionEvent e, int index, bool otherPointersDown)
    {
        var mode = _mode;
        var downX = _downX;
        var downY = _downY;
        var isTap = mode == Mode.Draw
            && StylusTapDetector.IsTap(_maxTravelPx, e.EventTime - _downTime, (v.Context is { } context ? ViewConfiguration.Get(context)?.ScaledTouchSlop : null) ?? 8);
        End(otherPointersDown);

        if (mode == Mode.Draw)
        {
            if (isTap)
            {
                // Not a stroke: no ink, no undo entry; act like a finger tap.
                _drawCancel();
                if (v.Width > 0 && v.Height > 0)
                {
                    _onTap?.Invoke(downX / v.Width, downY / v.Height);
                }
            }
            else
            {
                if (index >= 0 && e.ActionMasked == MotionEventActions.Up)
                {
                    _drawMove(Dp(v, e.GetX(index)), Dp(v, e.GetY(index)));
                }

                _drawEnd();
            }
        }
        else if (mode == Mode.Erase)
        {
            _eraseEnd();
        }
    }

    private void Abort()
    {
        var mode = _mode;
        End(otherPointersDown: false);
        if (mode == Mode.Draw)
        {
            _drawCancel();
        }
        else if (mode == Mode.Erase)
        {
            _eraseEnd();
        }
    }

    private void End(bool otherPointersDown)
    {
        _mode = Mode.None;
        _ignoreUntilDown = otherPointersDown;
        _view?.Parent?.RequestDisallowInterceptTouchEvent(false);
        _view = null;
    }

    private static float Dp(View v, float px)
    {
        var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
        return px / density;
    }
}
