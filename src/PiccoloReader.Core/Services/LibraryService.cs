using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Services;

public class LibraryService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;
    private readonly ExternalLibrarySync? _externalSync;
    private readonly IPageThumbnailStore? _thumbnails;

    public LibraryService(AppDatabase database, IAppStorageProvider storageProvider, ExternalLibrarySync? externalSync = null, IPageThumbnailStore? thumbnails = null)
    {
        _database = database;
        _storageProvider = storageProvider;
        _externalSync = externalSync;
        _thumbnails = thumbnails;
    }

    public Task<List<Folder>> GetFoldersAsync() =>
        _database.Connection.Table<Folder>().OrderBy(f => f.Name).ToListAsync();

    public Task<List<Sheet>> GetSheetsAsync(int? folderId) =>
        _database.Connection.Table<Sheet>()
            .Where(s => s.FolderId == folderId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Title)
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

    // deleteExternalCopies only matters with deleteSheets: false keeps the PDFs in the
    // external folder (and so their directory) and only drops them from the library.
    public async Task DeleteFolderAsync(int folderId, bool deleteSheets, bool deleteExternalCopies = false)
    {
        var sheets = await GetSheetsAsync(folderId);

        foreach (var sheet in sheets)
        {
            if (deleteSheets)
            {
                await DeleteSheetAsync(sheet, deleteExternalCopies);
            }
            else
            {
                sheet.FolderId = null;
                sheet.SortOrder = await SheetOrdering.NextSortOrderAsync(_database.Connection, null);
                await _database.Connection.ExecuteAsync("UPDATE Sheet SET FolderId = NULL, SortOrder = ? WHERE Id = ?", sheet.SortOrder, sheet.Id);
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
        sheet.SortOrder = await SheetOrdering.NextSortOrderAsync(_database.Connection, targetFolderId);
        await _database.Connection.ExecuteAsync("UPDATE Sheet SET FolderId = ?, SortOrder = ? WHERE Id = ?", targetFolderId, sheet.SortOrder, sheet.Id);

        if (_externalSync is not null)
        {
            await _externalSync.SyncSheetAsync(sheet, requestAccess: false);
            _externalSync.NotifyChanged();
        }
    }

    // Persists a full order for one folder (root = null) in a single transaction:
    // each id gets its index as SortOrder.
    public async Task SetSheetOrderAsync(int? folderId, IReadOnlyList<int> sheetIds)
    {
        var ids = sheetIds.ToArray();
        await _database.Connection.RunInTransactionAsync(connection =>
        {
            for (var i = 0; i < ids.Length; i++)
            {
                connection.Execute("UPDATE Sheet SET SortOrder = ? WHERE Id = ? AND FolderId IS ?", i, ids[i], folderId);
            }
        });

        _externalSync?.NotifyChanged();
    }

    // Always removes the row, thumbnails and the private copy. The visible copy in
    // the external folder is deleted only when asked; the snapshot is rewritten
    // either way so it no longer lists the sheet.
    public async Task DeleteSheetAsync(Sheet sheet, bool deleteExternalCopy)
    {
        await _database.Connection.DeleteAsync(sheet);
        if (_thumbnails is not null)
        {
            await _thumbnails.DeleteForSheetAsync(sheet.Id);
        }

        var filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        if (_externalSync is not null)
        {
            if (deleteExternalCopy)
            {
                await _externalSync.RemoveSheetAsync(sheet);
            }
            else
            {
                _externalSync.NotifyChanged();
            }
        }
    }
}
