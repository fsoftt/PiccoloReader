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
// of the gesture. Pinch itself is delegated to Android's own
// ScaleGestureDetector rather than hand-rolled span math.
public sealed class PageContainerTouchListener : Java.Lang.Object, View.IOnTouchListener
{
    private const float TapSlopDp = 10f;
    private const long TapMaxDurationMs = 300;

    private readonly ScaleGestureDetector _scaleDetector;
    private readonly Action<double, double> _onTap;
    private readonly Action<double, double> _onPanUpdate;
    private readonly Action _onPanEnd;
    private readonly float _density;

    private View? _touchedView;
    private bool _isPossiblePanOrTap;
    private float _panStartX;
    private float _panStartY;
    private long _downTimeMs;

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
        _onPanUpdate = onPanUpdate;
        _onPanEnd = onPanEnd;
        _density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        _scaleDetector = new ScaleGestureDetector(context, new ScaleListener(this, onPinchStart, onPinchUpdate, onPinchEnd));
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (v is null || e is null)
        {
            return false;
        }

        _touchedView = v;
        _scaleDetector.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _isPossiblePanOrTap = true;
                _panStartX = e.GetX();
                _panStartY = e.GetY();
                _downTimeMs = Java.Lang.JavaSystem.CurrentTimeMillis();
                break;

            case MotionEventActions.PointerDown:
                // A second finger arrived - this is a pinch, not a pan/tap.
                // ScaleGestureDetector (fed every event above) takes over;
                // don't apply any further pan deltas for this gesture.
                _isPossiblePanOrTap = false;
                break;

            case MotionEventActions.Move:
                if (_isPossiblePanOrTap && e.PointerCount == 1)
                {
                    _onPanUpdate((e.GetX() - _panStartX) / _density, (e.GetY() - _panStartY) / _density);
                }

                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isPossiblePanOrTap)
                {
                    FinishPanOrTap(v, e);
                    _isPossiblePanOrTap = false;
                }

                break;
        }

        return true;
    }

    private void FinishPanOrTap(View v, MotionEvent e)
    {
        var totalXDp = (e.GetX() - _panStartX) / _density;
        var totalYDp = (e.GetY() - _panStartY) / _density;
        var travelDp = Math.Sqrt(totalXDp * totalXDp + totalYDp * totalYDp);
        var durationMs = Java.Lang.JavaSystem.CurrentTimeMillis() - _downTimeMs;

        if (travelDp < TapSlopDp && durationMs < TapMaxDurationMs && v.Width > 0 && v.Height > 0)
        {
            _onTap(e.GetX() / v.Width, e.GetY() / v.Height);
        }
        else
        {
            _onPanUpdate(totalXDp, totalYDp);
            _onPanEnd();
        }
    }

    private sealed class ScaleListener : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private readonly PageContainerTouchListener _owner;
        private readonly Action _onPinchStart;
        private readonly Action<double, double, double> _onPinchUpdate;
        private readonly Action _onPinchEnd;

        public ScaleListener(
            PageContainerTouchListener owner,
            Action onPinchStart,
            Action<double, double, double> onPinchUpdate,
            Action onPinchEnd)
        {
            _owner = owner;
            _onPinchStart = onPinchStart;
            _onPinchUpdate = onPinchUpdate;
            _onPinchEnd = onPinchEnd;
        }

        public override bool OnScaleBegin(ScaleGestureDetector? detector)
        {
            _onPinchStart();
            return true;
        }

        public override bool OnScale(ScaleGestureDetector? detector)
        {
            if (detector is not null && _owner._touchedView is { Width: > 0, Height: > 0 } view)
            {
                _onPinchUpdate(detector.ScaleFactor, detector.FocusX / view.Width, detector.FocusY / view.Height);
            }

            return true;
        }

        public override void OnScaleEnd(ScaleGestureDetector? detector) => _onPinchEnd();
    }
}
