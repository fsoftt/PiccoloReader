using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class PdfImportService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;

    public PdfImportService(AppDatabase database, IAppStorageProvider storageProvider)
    {
        _database = database;
        _storageProvider = storageProvider;
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
        return sheet;
    }
}
