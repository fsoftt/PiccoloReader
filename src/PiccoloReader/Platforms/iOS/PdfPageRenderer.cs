using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Platforms.iOS;

// Deferred: no Mac/iOS simulator available to build or test a real PDFKit
// implementation against (see "Scope" in the PDF Viewer plan). This stub
// exists only so the app still compiles for net10.0-ios.
public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx, PageCrop? crop = null) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");

    public Task<byte[]> RenderPageRegionAsync(
        string filePath,
        int pageIndex,
        double regionX,
        double regionY,
        double regionWidth,
        double regionHeight,
        int outputWidthPx,
        int outputHeightPx,
        PageCrop? crop,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");
}
