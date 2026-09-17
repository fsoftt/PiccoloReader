using Android.Content;
using Android.Views;
using View = Android.Views.View;

namespace PiccoloReader.Platforms.Android;

// Single-finger drag tracker for elements nested inside PageContainer's
// zoom transform (SelectionBorder for icon move, ResizeHandle for icon
// resize - see SheetViewerPage.AttachAndroidSelectionDragListeners).
//
// Uses MotionEvent.RawX/RawY - true screen coordinates, always unaffected
// by any ancestor's transform, no matter how many are stacked - instead
// of MAUI's PanGestureRecognizer. TotalX/TotalY from that recognizer get
// shrunk by PageContainer's live Scale for the same reason PageContainer's
// own GetX/GetY did before PageContainerTouchListener replaced it there
// (confirmed empirically: identical drags measured smaller as zoom
// increased). RawX/RawY sidestep the problem entirely rather than
// needing to reconstruct anything, since a single-finger drag only needs
// pointer 0, and RawX/RawY report that pointer's true screen position
// regardless of the SelectionBorder/SelectionOverlay/PageContainer
// transform chain above it (which, unlike PageContainer's own case,
// includes a second live-updating transform - SelectionOverlay's own
// TranslationX/Y, repositioned every Running frame to follow the drag -
// that a reconstruction would have had to account for too).
//
// Density-converts to DP so callers get the same units
// PanUpdatedEventArgs.TotalX/TotalY would have provided.
public sealed class SingleDragTouchListener : Java.Lang.Object, View.IOnTouchListener
{
    private readonly Action _onStarted;
    private readonly Action<double, double> _onRunning;
    private readonly Action _onEnded;
    private readonly float _density;

    private bool _isDragging;
    private float _startX;
    private float _startY;

    public SingleDragTouchListener(Context context, Action onStarted, Action<double, double> onRunning, Action onEnded)
    {
        _onStarted = onStarted;
        _onRunning = onRunning;
        _onEnded = onEnded;
        _density = context.Resources?.DisplayMetrics?.Density ?? 1f;
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
                _isDragging = true;
                _startX = e.RawX;
                _startY = e.RawY;
                _onStarted();
                break;

            case MotionEventActions.Move:
                if (_isDragging)
                {
                    _onRunning((e.RawX - _startX) / _density, (e.RawY - _startY) / _density);
                }

                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isDragging)
                {
                    _isDragging = false;
                    _onEnded();
                }

                break;
        }

        return true;
    }
}
