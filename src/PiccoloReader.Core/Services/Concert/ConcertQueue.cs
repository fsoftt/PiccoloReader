namespace PiccoloReader.Core.Services.Concert;

public class ConcertQueue : IConcertQueue
{
    private readonly object _gate = new();
    private int[] _ids = Array.Empty<int>();

    public IReadOnlyList<int> SheetIds
    {
        get
        {
            lock (_gate)
            {
                return _ids;
            }
        }
    }

    public void SetQueue(IEnumerable<int> sheetIds)
    {
        var copy = sheetIds.ToArray();
        lock (_gate)
        {
            _ids = copy;
        }
    }

    public void Clear() => SetQueue(Array.Empty<int>());

    public int? NextAfter(int sheetId) => Neighbour(sheetId, 1);

    public int? PreviousBefore(int sheetId) => Neighbour(sheetId, -1);

    private int? Neighbour(int sheetId, int delta)
    {
        int[] ids;
        lock (_gate)
        {
            ids = _ids;
        }

        var index = Array.IndexOf(ids, sheetId);
        if (index < 0)
        {
            return null;
        }

        var target = index + delta;
        return target >= 0 && target < ids.Length ? ids[target] : null;
    }
}
