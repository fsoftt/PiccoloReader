namespace PiccoloReader.Core.Services.PageTurn;

// Platform-neutral keys a Bluetooth page-turn pedal or keyboard sends.
// Volume keys are deliberately absent: they stay the system's.
public enum PageTurnKey
{
    Other,
    PageDown,
    PageUp,
    DpadRight,
    DpadLeft,
    DpadDown,
    DpadUp,
    Space,
    Enter,
    NumpadEnter,
    MediaNext,
    MediaPrevious
}

public enum PageTurnIntent
{
    None,
    Next,
    Previous
}

public static class PedalKeyMap
{
    public static PageTurnIntent Resolve(PageTurnKey key, bool shift) => key switch
    {
        PageTurnKey.Space => shift ? PageTurnIntent.Previous : PageTurnIntent.Next,
        PageTurnKey.PageDown or PageTurnKey.DpadRight or PageTurnKey.DpadDown
            or PageTurnKey.Enter or PageTurnKey.NumpadEnter or PageTurnKey.MediaNext => PageTurnIntent.Next,
        PageTurnKey.PageUp or PageTurnKey.DpadLeft or PageTurnKey.DpadUp
            or PageTurnKey.MediaPrevious => PageTurnIntent.Previous,
        _ => PageTurnIntent.None
    };
}
