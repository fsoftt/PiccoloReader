using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class BookmarkService
{
    private readonly AppDatabase _database;

    public BookmarkService(AppDatabase database)
    {
        _database = database;
    }

    public Task<List<Bookmark>> GetBookmarksAsync(int sheetId) =>
        _database.Connection.Table<Bookmark>()
            .Where(b => b.SheetId == sheetId)
            .OrderBy(b => b.PageIndex)
            .ToListAsync();

    public async Task<Bookmark> AddBookmarkAsync(int sheetId, int pageIndex)
    {
        var bookmark = new Bookmark
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(bookmark);
        return bookmark;
    }
}
