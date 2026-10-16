namespace PiccoloReader.Core.Services.PageTurn;

// Seam between the platform key handling and the visible viewer page.
// The page subscribes while it is on screen; with no subscriber a key is
// not handled, so it passes through to the rest of the app untouched.
public interface IPageTurnInput
{
    event Action<PageTurnIntent>? Requested;

    bool HasListener { get; }

    // Returns true when the key maps to a page turn and a page took it.
    bool Handle(PageTurnKey key, bool shift);
}

public sealed class PageTurnInput : IPageTurnInput
{
    public event Action<PageTurnIntent>? Requested;

    public bool HasListener => Requested is not null;

    public bool Handle(PageTurnKey key, bool shift)
    {
        var intent = PedalKeyMap.Resolve(key, shift);
        if (intent == PageTurnIntent.None || Requested is not { } handler)
        {
            return false;
        }

        handler(intent);
        return true;
    }
}
