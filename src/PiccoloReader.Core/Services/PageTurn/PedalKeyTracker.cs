namespace PiccoloReader.Core.Services.PageTurn;

public enum PedalKeyDecision
{
    // The key has not been seen yet: evaluate the guards and the page, then
    // report the outcome with Record (for a Down).
    TryTurn,

    // Part of a press the page already took: consume it.
    Swallow,

    // Part of a press that was passed through (typing, focused button, an
    // overlay): leave it alone.
    PassThrough
}

// Follows page-turn keys per (device, key) so a held pedal turns one page
// and so a press whose Down the platform ate still turns it.
//
// Android eats the ACTION_DOWN of a navigation key (Page Up/Down, D-pad...)
// that leaves touch mode, but its ACTION_UP still arrives. An Up with no
// recorded Down is therefore a press nobody has seen: TryTurn. A Down the
// page declined is recorded as passed through, so its Up does not turn.
// Fixed size and allocation free; when full the oldest entry is dropped.
public sealed class PedalKeyTracker
{
    private const int Capacity = 8;
    private const byte Handled = 1;
    private const byte PassedThrough = 2;

    private readonly long[] _ids = new long[Capacity];
    private readonly byte[] _states = new byte[Capacity];
    private int _count;

    public static long IdOf(int deviceId, int keyCode) => ((long)deviceId << 32) | (uint)keyCode;

    public PedalKeyDecision OnDown(long id, int repeatCount)
    {
        if (repeatCount == 0)
        {
            // A fresh press always starts clean, whatever happened to the
            // previous key-up.
            Remove(id);
            return PedalKeyDecision.TryTurn;
        }

        var index = IndexOf(id);
        if (index < 0)
        {
            // Repeat of a press whose Down never reached us (eaten): treat
            // it as the press, so holding still turns once.
            return PedalKeyDecision.TryTurn;
        }

        return _states[index] == Handled ? PedalKeyDecision.Swallow : PedalKeyDecision.PassThrough;
    }

    // Outcome of a TryTurn Down: the page took it (handled) or it passes through.
    public void Record(long id, bool handled)
    {
        Remove(id);
        if (_count == Capacity)
        {
            Array.Copy(_ids, 1, _ids, 0, Capacity - 1);
            Array.Copy(_states, 1, _states, 0, Capacity - 1);
            _count--;
        }

        _ids[_count] = id;
        _states[_count] = handled ? Handled : PassedThrough;
        _count++;
    }

    public PedalKeyDecision OnUp(long id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            return PedalKeyDecision.TryTurn; // orphan Up: its Down was eaten
        }

        var handled = _states[index] == Handled;
        RemoveAt(index);
        return handled ? PedalKeyDecision.Swallow : PedalKeyDecision.PassThrough;
    }

    public void Clear() => _count = 0;

    private int IndexOf(long id)
    {
        for (var i = 0; i < _count; i++)
        {
            if (_ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void Remove(long id)
    {
        var index = IndexOf(id);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    private void RemoveAt(int index)
    {
        _count--;
        _ids[index] = _ids[_count];
        _states[index] = _states[_count];
    }
}
