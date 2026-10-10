using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Services;

public class LibraryService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;
    private readonly ExternalLibrarySync? _externalSync;

    public LibraryService(AppDatabase database, IAppStorageProvider storageProvider, ExternalLibrarySync? externalSync = null)
    {
        _database = database;
        _storageProvider = storageProvider;
        _externalSync = externalSync;
    }

    public Task<List<Folder>> GetFoldersAsync() =>
        _database.Connection.Table<Folder>().OrderBy(f => f.Name).ToListAsync();

    public Task<List<Sheet>> GetSheetsAsync(int? folderId) =>
        _database.Connection.Table<Sheet>()
            .Where(s => s.FolderId == folderId)
            .OrderBy(s => s.Title)
            .ToListAsync();

    public async Task<Dictionary<int, int>> GetSheetCountsByFolderAsync()
    {
        var sheets = await _database.Connection.Table<Sheet>().Where(s => s.FolderId != null).ToListAsync();
        return sheets.GroupBy(s => s.FolderId!.Value).ToDictionary(g => g.Key, g => g.Count());
    }

    public Task<Sheet> GetSheetAsync(int sheetId) =>
        _database.Connection.Table<Sheet>().Where(s => s.Id == sheetId).FirstAsync();

    // Targeted UPDATEs: the caller's Sheet may be stale (e.g. missing the
    // ExternalPath a background sync filled in) and must not overwrite it.
    public async Task UpdateSheetPageCountAsync(Sheet sheet, int pageCount)
    {
        sheet.PageCount = pageCount;
        await _database.Connection.ExecuteAsync("UPDATE Sheet SET PageCount = ? WHERE Id = ?", pageCount, sheet.Id);
        _externalSync?.NotifyChanged();
    }

    public Task UpdateSheetLastViewedPageAsync(Sheet sheet, int pageIndex)
    {
        sheet.LastViewedPageIndex = pageIndex;
        return _database.Connection.ExecuteAsync("UPDATE Sheet SET LastViewedPageIndex = ? WHERE Id = ?", pageIndex, sheet.Id);
    }

    public async Task<Folder> CreateFolderAsync(string name)
    {
        var trimmedName = FolderNameNormalizer.Normalize(name);
        if (trimmedName is null)
        {
            throw new ArgumentException("Folder name cannot be empty or whitespace.", nameof(name));
        }

        var folder = new Folder { Name = trimmedName, DateAdded = DateTime.UtcNow };
        await _database.Connection.InsertAsync(folder);
        _externalSync?.NotifyChanged();
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
                await _database.Connection.ExecuteAsync("UPDATE Sheet SET FolderId = NULL WHERE Id = ?", sheet.Id);
            }
        }

        var folder = await _database.Connection.Table<Folder>()
            .Where(f => f.Id == folderId)
            .FirstAsync();
        await _database.Connection.DeleteAsync(folder);
        var folderName = folder.Name;

        if (_externalSync is not null)
        {
            // Sheets kept in the library moved to the root: move their files too.
            if (!deleteSheets)
            {
                foreach (var sheet in sheets)
                {
                    await _externalSync.SyncSheetAsync(sheet, requestAccess: false);
                }
            }

            await _externalSync.RemoveFolderDirectoryAsync(folderName);
            _externalSync.NotifyChanged();
        }
    }

    public async Task MoveSheetAsync(Sheet sheet, int? targetFolderId)
    {
        sheet.FolderId = targetFolderId;
        await _database.Connection.ExecuteAsync("UPDATE Sheet SET FolderId = ? WHERE Id = ?", targetFolderId, sheet.Id);

        if (_externalSync is not null)
        {
            await _externalSync.SyncSheetAsync(sheet, requestAccess: false);
            _externalSync.NotifyChanged();
        }
    }

    public async Task DeleteSheetAsync(Sheet sheet)
    {
        await _database.Connection.DeleteAsync(sheet);

        var filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        if (_externalSync is not null)
        {
            await _externalSync.RemoveSheetAsync(sheet);
        }
    }
}
