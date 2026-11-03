namespace PiccoloReader.Core.Services.Concert;

/// <summary>
/// Ordered sheet ids of the list the user opened a sheet from (library root or
/// a folder, in its current sort), so the viewer can step to the next score.
/// </summary>
public interface IConcertQueue
{
    IReadOnlyList<int> SheetIds { get; }

    void SetQueue(IEnumerable<int> sheetIds);

    void Clear();

    /// <summary>Id after <paramref name="sheetId"/>, or null at the end or when it is not queued.</summary>
    int? NextAfter(int sheetId);

    /// <summary>Id before <paramref name="sheetId"/>, or null at the start or when it is not queued.</summary>
    int? PreviousBefore(int sheetId);
}
