using System.Text;

namespace PiccoloReader.Core.Services;

public sealed record SharedPdfImportResult(int ImportedCount, int FailedCount, string? FirstImportedTitle);

// Imports PDFs handed over by other apps (open-with / share). The platform
// layer first copies each shared stream to a local temp file, because the
// read grant of a shared content URI is only temporary. This service then
// funnels those files through the regular PdfImportService.
public class SharedPdfImportService
{
    private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-");

    private readonly PdfImportService _importService;

    public SharedPdfImportService(PdfImportService importService)
    {
        _importService = importService;
    }

    // Files must start with the PDF magic bytes (a few producers prepend
    // junk, but the spec allows it only within the first 1024 bytes).
    public static bool IsPdf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[1024];
            var read = stream.Read(buffer, 0, buffer.Length);
            return buffer.AsSpan(0, read).IndexOf(PdfHeader) >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Makes a provider-supplied display name safe to use as a file name and
    // guarantees a non-empty ".pdf" name (the title is derived from it).
    public static string SanitizeFileName(string? displayName, string fallbackName)
    {
        var name = displayName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            name = Path.GetFileName(name.Replace('\\', '/'));
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
        }

        var title = Path.GetFileNameWithoutExtension(name ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            title = fallbackName;
        }

        return title + ".pdf";
    }

    // Imports every valid PDF into the destination (null = library root).
    // Temp files are always deleted. One bad file never blocks the others.
    public async Task<SharedPdfImportResult> ImportAsync(IEnumerable<string> tempFilePaths, int? folderId)
    {
        var imported = 0;
        var failed = 0;
        string? firstTitle = null;

        foreach (var path in tempFilePaths)
        {
            try
            {
                if (!IsPdf(path))
                {
                    failed++;
                    continue;
                }

                var sheet = await _importService.ImportAsync(path, folderId);
                imported++;
                firstTitle ??= sheet.Title;
            }
            catch (Exception)
            {
                failed++;
            }
            finally
            {
                TryDelete(path);
            }
        }

        return new SharedPdfImportResult(imported, failed, firstTitle);
    }

    // Discards shared files without importing (the user cancelled).
    public static void Discard(IEnumerable<string> tempFilePaths)
    {
        foreach (var path in tempFilePaths)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: it lives in the cache directory anyway.
        }
    }
}
