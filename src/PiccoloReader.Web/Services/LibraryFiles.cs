using System.Security.Cryptography;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// Bridges Core's recovery/sync code, which reads and writes PDFs under IAppStorageProvider.SheetsDirectory
// (the in-memory file system here), and the web app's real PDF store, OPFS.
public sealed class LibraryFiles(AppDatabase db, IAppStorageProvider storage, BrowserInterop browser,
    PdfCanvasRenderer pdf, PersistentDatabase persist)
{
    // Moves PDFs that LibraryRecovery copied into the in-memory sheets directory to OPFS.
    public async Task AdoptStagedFilesAsync()
    {
        if (!Directory.Exists(storage.SheetsDirectory)) return;
        // Only the files staged right now; their sheets are looked up one by one, so calling
        // this after every imported PDF stays cheap.
        foreach (var path in Directory.GetFiles(storage.SheetsDirectory))
        {
            var fileName = Path.GetFileName(path);
            var sheet = await db.Connection.Table<Sheet>().Where(s => s.FileName == fileName).FirstOrDefaultAsync();
            if (sheet is null) continue;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var pages = await pdf.ImportAsync(sheet.FileName, bytes);
                if (sheet.PageCount <= 0 && pages > 0)
                {
                    sheet.PageCount = pages;
                    await db.Connection.UpdateAsync(sheet);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not store '{sheet.Title}': {ex.Message}");
            }
            finally
            {
                File.Delete(path);
            }
            await Task.Delay(1);
        }
        persist.ScheduleSave();
    }

    // Sheets imported before hashes were recorded get theirs (needed to merge backups by content).
    public async Task EnsureHashesAsync(IProgress<(int Current, int Total)>? progress = null)
    {
        var missing = await db.Connection.Table<Sheet>().Where(s => s.ContentHash == null).ToListAsync();
        for (var i = 0; i < missing.Count; i++)
        {
            progress?.Report((i, missing.Count));
            var bytes = await browser.ReadFileAsync("sheets/" + missing[i].FileName);
            if (bytes is null) continue;
            missing[i].ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await db.Connection.UpdateAsync(missing[i]);
            await Task.Delay(1);
        }
        if (missing.Count > 0) persist.ScheduleSave(notify: false);
    }
}
