using PiccoloReader.Core.Services;

namespace PiccoloReader.Platforms.iOS;

// Deferred: no Mac/iOS simulator available to build or test a real PDFKit
// implementation against (see "Scope" in the PDF Viewer plan). This stub
// exists only so the app still compiles for net10.0-ios.
public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");
}
