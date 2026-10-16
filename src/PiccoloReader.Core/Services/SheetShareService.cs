using System.Text;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

// Prepares the PDF file to share for a sheet: the original, unchanged, when
// it has no annotations (best quality, no re-encoding), otherwise a copy with
// the annotations drawn in. Files go to a scratch folder that is emptied on
// each call, so at most one shared file is ever kept.
public class SheetShareService
{
    public const string FallbackFileName = "sheet";

    private readonly IAppStorageProvider _storage;
    private readonly AnnotationService _annotations;
    private readonly IAnnotatedPdfExporter? _exporter;
    private readonly string _shareDirectory;

    public SheetShareService(
        IAppStorageProvider storage,
        AnnotationService annotations,
        IAnnotatedPdfExporter? exporter,
        string shareDirectory)
    {
        _storage = storage;
        _annotations = annotations;
        _exporter = exporter;
        _shareDirectory = shareDirectory;
    }

    public bool IsSupported => _exporter is not null;

    public async Task<string> PrepareAsync(
        Sheet sheet,
        (double Width, double Height) legacyReferenceArea,
        CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(_storage.SheetsDirectory, sheet.FileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The sheet PDF is missing.", source);
        }

        CleanUp();
        Directory.CreateDirectory(_shareDirectory);
        var destination = Path.Combine(_shareDirectory, BuildFileName(sheet.Title));

        if (_exporter is null || !await _annotations.HasAnnotationsAsync(sheet.Id))
        {
            File.Copy(source, destination, overwrite: true);
            return destination;
        }

        try
        {
            await _exporter.ExportAsync(source, sheet.Id, destination, legacyReferenceArea, cancellationToken);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        return destination;
    }

    // Deletes files left by earlier shares.
    public void CleanUp()
    {
        if (!Directory.Exists(_shareDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_shareDirectory))
        {
            TryDelete(file);
        }
    }

    // "<title>.pdf" with characters that are invalid in file names replaced,
    // an existing ".pdf" extension not doubled, and a fallback for an empty title.
    public static string BuildFileName(string? title)
    {
        var name = (title ?? string.Empty).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) || c == '/' ? '_' : c);
        }

        name = builder.ToString().Trim().TrimEnd('.').Trim();
        if (name.Length == 0 || name.All(c => c == '_'))
        {
            name = FallbackFileName;
        }

        if (name.Length > 100)
        {
            name = name[..100].TrimEnd();
        }

        return name + ".pdf";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
