using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// Library mutations for the web app: Core's LibraryService for the database work,
// plus the OPFS side (PDF and thumbnail files) and persisting the DB afterwards.
public sealed class WebLibrary(AppDatabase db, LibraryService library, PdfCanvasRenderer pdf, PersistentDatabase persist)
{
    public const long MaxPdfBytes = 200L * 1024 * 1024;

    public async Task<Sheet> ImportAsync(string fileName, Stream content, int? folderId)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        // Unique stored name so two PDFs with the same name never collide.
        var stored = $"{Guid.NewGuid():N}.pdf";
        var pages = await pdf.ImportAsync(stored, ms.ToArray());
        var sheet = new Sheet
        {
            FolderId = folderId,
            Title = Path.GetFileNameWithoutExtension(fileName),
            FileName = stored,
            PageCount = pages,
            DateAdded = DateTime.UtcNow,
            SortOrder = await db.Connection.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Sheet WHERE FolderId IS ?", folderId),
        };
        await db.Connection.InsertAsync(sheet);
        persist.ScheduleSave();
        return sheet;
    }

    public async Task CreateFolderAsync(string name)
    {
        await library.CreateFolderAsync(name);
        persist.ScheduleSave();
    }

    public async Task RenameSheetAsync(Sheet sheet, string title)
    {
        await library.RenameSheetAsync(sheet, title);
        persist.ScheduleSave();
    }

    public async Task MoveSheetAsync(Sheet sheet, int? folderId)
    {
        await library.MoveSheetAsync(sheet, folderId);
        persist.ScheduleSave();
    }

    public async Task DeleteSheetAsync(Sheet sheet)
    {
        await library.DeleteSheetAsync(sheet, deleteExternalCopy: false);
        await pdf.DeleteFilesAsync(sheet.FileName, sheet.Id);
        await DeleteSheetDataAsync(sheet.Id);
        persist.ScheduleSave();
    }

    public async Task DeleteFolderAsync(Folder folder, bool deleteSheets)
    {
        var sheets = await library.GetSheetsAsync(folder.Id);
        await library.DeleteFolderAsync(folder.Id, deleteSheets);
        if (deleteSheets)
        {
            foreach (var s in sheets)
            {
                await pdf.DeleteFilesAsync(s.FileName, s.Id);
                await DeleteSheetDataAsync(s.Id);
            }
        }
        persist.ScheduleSave();
    }

    public async Task SetOrderAsync(int? folderId, IReadOnlyList<int> ids)
    {
        await library.SetSheetOrderAsync(folderId, ids);
        persist.ScheduleSave();
    }

    // Bookmarks and crops belong to the sheet; Core's DeleteSheetAsync leaves them behind.
    private async Task DeleteSheetDataAsync(int sheetId)
    {
        await db.Connection.ExecuteAsync("DELETE FROM Bookmark WHERE SheetId = ?", sheetId);
        await db.Connection.ExecuteAsync("DELETE FROM SheetPageCrop WHERE SheetId = ?", sheetId);
        await db.Connection.ExecuteAsync("DELETE FROM Annotation WHERE SheetId = ?", sheetId);
    }
}
