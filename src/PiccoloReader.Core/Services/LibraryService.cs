using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class LibraryService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;

    public LibraryService(AppDatabase database, IAppStorageProvider storageProvider)
    {
        _database = database;
        _storageProvider = storageProvider;
    }

    public Task<List<Folder>> GetFoldersAsync() =>
        _database.Connection.Table<Folder>().OrderBy(f => f.Name).ToListAsync();

    public Task<List<Sheet>> GetSheetsAsync(int? folderId) =>
        _database.Connection.Table<Sheet>()
            .Where(s => s.FolderId == folderId)
            .OrderBy(s => s.Title)
            .ToListAsync();

    public async Task<Folder> CreateFolderAsync(string name)
    {
        var folder = new Folder { Name = name };
        await _database.Connection.InsertAsync(folder);
        return folder;
    }

    public async Task DeleteFolderAsync(int folderId, bool deleteSheets)
    {
        var sheets = await GetSheetsAsync(folderId);

        foreach (var sheet in sheets)
        {
            if (deleteSheets)
            {
                await DeleteSheetAsync(sheet);
            }
            else
            {
                sheet.FolderId = null;
                await _database.Connection.UpdateAsync(sheet);
            }
        }

        var folder = await _database.Connection.Table<Folder>()
            .Where(f => f.Id == folderId)
            .FirstAsync();
        await _database.Connection.DeleteAsync(folder);
    }

    public Task MoveSheetAsync(Sheet sheet, int? targetFolderId)
    {
        sheet.FolderId = targetFolderId;
        return _database.Connection.UpdateAsync(sheet);
    }

    public async Task DeleteSheetAsync(Sheet sheet)
    {
        await _database.Connection.DeleteAsync(sheet);

        var filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
