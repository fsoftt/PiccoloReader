using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Services;

public class BookmarkService
{
    private readonly AppDatabase _database;
    private readonly ILibraryChangeNotifier? _notifier;

    public BookmarkService(AppDatabase database, ILibraryChangeNotifier? notifier = null)
    {
        _database = database;
        _notifier = notifier;
    }

    public Task<List<Bookmark>> GetBookmarksAsync(int sheetId) =>
        _database.Connection.Table<Bookmark>()
            .Where(b => b.SheetId == sheetId)
            .OrderBy(b => b.PageIndex)
            .ToListAsync();

    public async Task<Bookmark> AddBookmarkAsync(int sheetId, int pageIndex, string? name = null)
    {
        var bookmark = new Bookmark
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(bookmark);
        _notifier?.NotifyChanged();
        return bookmark;
    }
}
