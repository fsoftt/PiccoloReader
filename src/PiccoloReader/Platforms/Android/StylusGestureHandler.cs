using Android.Views;
using PiccoloReader.Core.Services.Stylus;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Stylus-specific part of a touch listener. Called first by the listener on
// every event; returns true when it consumed the event, so the normal finger
// handling (pan, pinch, tap) never sees a stylus gesture.
//
// The role of a gesture is decided at ACTION_DOWN (see StylusInputClassifier):
// - Draw: only handled here when canDraw() is true (direct drawing on the
//   page). Otherwise the gesture is left to the caller (the Pencil
//   DrawingView draws it natively).
// - Erase (eraser tip, or stylus side button held): handled here whenever
//   canErase() is true.
// If the side button is pressed in the middle of a stroke, the stroke is
// dropped (own stroke: drawCancel; native stroke: cancelNativeStroke) and the
// rest of the gesture erases.
// While a gesture is handled here, every other pointer (a resting palm,
// fingers) is ignored.
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
    private readonly Action<View, MotionEvent>? _cancelNativeStroke;

    private Mode _mode;
    private int _pointerId;
    private bool _nativeStylusGesture;

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
        Action<View, MotionEvent>? cancelNativeStroke = null)
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
        _cancelNativeStroke = cancelNativeStroke;
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

    public bool Handle(View v, MotionEvent e)
    {
        var action = e.ActionMasked;

        if (_mode == Mode.None)
        {
            if (action == MotionEventActions.Down)
            {
                _nativeStylusGesture = false;
                var role = RoleOf(e, 0);
                if (role == StylusRole.Draw && _canDraw())
                {
                    Begin(v, e, 0, Mode.Draw);
                    return true;
                }

                if (role == StylusRole.Erase && _canErase())
                {
                    Begin(v, e, 0, Mode.Erase);
                    return true;
                }

                // A plain stylus stroke left to the native view: watch it for
                // the side button being pressed later.
                _nativeStylusGesture = role == StylusRole.Draw && _canErase();
                return false;
            }

            if (_nativeStylusGesture && action == MotionEventActions.Move && e.PointerCount == 1 && RoleOf(e, 0) == StylusRole.Erase)
            {
                _nativeStylusGesture = false;
                _cancelNativeStroke?.Invoke(v, e);
                Begin(v, e, 0, Mode.Erase);
                return true;
            }

            if (action is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                _nativeStylusGesture = false;
            }

            return false;
        }

        // A gesture is being handled here.
        if (action == MotionEventActions.Down)
        {
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
                    MoveTo(v, e, index);
                }

                Finish();
                break;

            case MotionEventActions.PointerUp:
                if (e.ActionIndex == index)
                {
                    Finish();
                }

                break;

            case MotionEventActions.Cancel:
                Abort();
                break;
        }

        return true;
    }

    private void Begin(View v, MotionEvent e, int index, Mode mode)
    {
        _mode = mode;
        _pointerId = e.GetPointerId(index);
        v.Parent?.RequestDisallowInterceptTouchEvent(true);
        var x = Dp(v, e.GetX(index));
        var y = Dp(v, e.GetY(index));
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

    private void MoveTo(View v, MotionEvent e, int index)
    {
        if (_mode == Mode.Draw)
        {
            _drawMove(Dp(v, e.GetX(index)), Dp(v, e.GetY(index)));
        }
        else
        {
            _eraseMove(Dp(v, e.GetX(index)), Dp(v, e.GetY(index)));
        }
    }

    private void Finish()
    {
        var mode = _mode;
        _mode = Mode.None;
        if (mode == Mode.Draw)
        {
            _drawEnd();
        }
        else if (mode == Mode.Erase)
        {
            _eraseEnd();
        }
    }

    private void Abort()
    {
        var mode = _mode;
        _mode = Mode.None;
        if (mode == Mode.Draw)
        {
            _drawCancel();
        }
        else if (mode == Mode.Erase)
        {
            _eraseEnd();
        }
    }

    private static float Dp(View v, float px)
    {
        var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
        return px / density;
    }
}
