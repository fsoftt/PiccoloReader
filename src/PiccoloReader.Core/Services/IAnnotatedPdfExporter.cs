namespace PiccoloReader.Core.Services;

// Writes a copy of a sheet's PDF with its annotations drawn into the pages.
// Implemented per platform (needs a PDF rasterizer); absent where sharing
// with annotations is not supported.
public interface IAnnotatedPdfExporter
{
    // legacyReferenceArea converts annotations saved in the old container
    // space; (0, 0) leaves such rows as they are.
    Task ExportAsync(
        string sourcePdfPath,
        int sheetId,
        string destinationPath,
        (double Width, double Height) legacyReferenceArea,
        CancellationToken cancellationToken = default);
}
