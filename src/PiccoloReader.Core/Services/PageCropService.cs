using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

public class PageCropService
{
    private readonly AppDatabase _database;

    public PageCropService(AppDatabase database)
    {
        _database = database;
    }

    // Valid crops of a sheet by page index; invalid or full rows are ignored.
    public async Task<Dictionary<int, PageCrop>> GetCropsAsync(int sheetId)
    {
        var rows = await _database.Connection.Table<SheetPageCrop>()
            .Where(c => c.SheetId == sheetId)
            .ToListAsync();

        var crops = new Dictionary<int, PageCrop>();
        foreach (var row in rows)
        {
            var crop = new PageCrop(row.Left, row.Top, row.Right, row.Bottom);
            if (crop.IsValid && !crop.IsFull)
            {
                crops[row.PageIndex] = crop;
            }
        }

        return crops;
    }

    // Saves the page's crop; a full-page (or invalid) crop removes it.
    public async Task SetCropAsync(int sheetId, int pageIndex, PageCrop crop)
    {
        var existing = await _database.Connection.Table<SheetPageCrop>()
            .Where(c => c.SheetId == sheetId && c.PageIndex == pageIndex)
            .ToListAsync();

        if (!crop.IsValid || crop.IsFull)
        {
            foreach (var row in existing)
            {
                await _database.Connection.DeleteAsync(row);
            }

            return;
        }

        var target = existing.FirstOrDefault() ?? new SheetPageCrop { SheetId = sheetId, PageIndex = pageIndex };
        target.Left = crop.Left;
        target.Top = crop.Top;
        target.Right = crop.Right;
        target.Bottom = crop.Bottom;

        if (target.Id == 0)
        {
            await _database.Connection.InsertAsync(target);
        }
        else
        {
            await _database.Connection.UpdateAsync(target);
        }
    }
}
