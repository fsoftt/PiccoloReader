using Android.OS;

namespace PiccoloReader.Platforms.Android;

// Fires once when a finger stays down for LongPressMs without leaving the
// touch slop. Touch listeners start it on Down, feed it Move positions and
// cancel it on Up, a second finger or a drag; Fired tells them to swallow the
// rest of the gesture so a long-press is never also a tap or a move.
public sealed class LongPressDetector
{
    public const long LongPressMs = 500;
    private const float SlopDp = 10f;

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly float _slopPx;
    private Java.Lang.IRunnable? _pending;
    private float _startX;
    private float _startY;

    public LongPressDetector(float density)
    {
        _slopPx = SlopDp * density;
    }

    // True once the callback has run for the current gesture.
    public bool Fired { get; private set; }

    // x/y are any consistent screen coordinates (RawX/RawY works anywhere).
    public void Start(float x, float y, Action onLongPress)
    {
        Cancel();
        Fired = false;
        _startX = x;
        _startY = y;
        _pending = new Java.Lang.Runnable(() =>
        {
            _pending = null;
            Fired = true;
            onLongPress();
        });
        _handler.PostDelayed(_pending, LongPressMs);
    }

    public void Move(float x, float y)
    {
        if (_pending is null)
        {
            return;
        }

        var dx = x - _startX;
        var dy = y - _startY;
        if (dx * dx + dy * dy > _slopPx * _slopPx)
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        if (_pending is not null)
        {
            _handler.RemoveCallbacks(_pending);
            _pending = null;
        }
    }

    // Cancel plus forgetting a fired press, at the end of a gesture.
    public void Reset()
    {
        Cancel();
        Fired = false;
    }
}
