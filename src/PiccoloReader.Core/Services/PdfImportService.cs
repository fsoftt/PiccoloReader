using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.ExternalLibrary;

namespace PiccoloReader.Core.Services;

public class PdfImportService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;
    private readonly ExternalLibrarySync? _externalSync;

    public PdfImportService(AppDatabase database, IAppStorageProvider storageProvider, ExternalLibrarySync? externalSync = null)
    {
        _database = database;
        _storageProvider = storageProvider;
        _externalSync = externalSync;
    }

    public async Task<Sheet> ImportAsync(string sourceFilePath, int? folderId)
    {
        Directory.CreateDirectory(_storageProvider.SheetsDirectory);

        var fileName = $"{Guid.NewGuid():N}.pdf";
        var destinationPath = Path.Combine(_storageProvider.SheetsDirectory, fileName);
        File.Copy(sourceFilePath, destinationPath);

        var sheet = new Sheet
        {
            FolderId = folderId,
            Title = Path.GetFileNameWithoutExtension(sourceFilePath),
            FileName = fileName,
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(sheet);

        if (_externalSync is not null)
        {
            // Never fails the import: an unsynced sheet is retried later.
            await _externalSync.SyncSheetAsync(sheet);
            _externalSync.NotifyChanged();
        }

        return sheet;
    }
}
