namespace PiccoloReader.Core.Services.PageTurn;

// Seam between the platform key handling and the visible viewer page.
// The page subscribes while it is on screen; with no subscriber a key is
// not handled, so it passes through to the rest of the app untouched.
// The page decides: its handler returns true when it took the press
// (including a no-op at the first/last page) and false to let the key
// through (editing, an overlay is open, not laid out yet).
public interface IPageTurnInput
{
    event Func<PageTurnIntent, bool>? Requested;

    bool HasListener { get; }

    // Returns true when the key maps to a page turn and a page took it.
    bool Handle(PageTurnKey key, bool shift);
}

public sealed class PageTurnInput : IPageTurnInput
{
    public event Func<PageTurnIntent, bool>? Requested;

    public bool HasListener => Requested is not null;

    public bool Handle(PageTurnKey key, bool shift)
    {
        var intent = PedalKeyMap.Resolve(key, shift);
        if (intent == PageTurnIntent.None || Requested is not { } handler)
        {
            return false;
        }

        return handler(intent);
    }
}
