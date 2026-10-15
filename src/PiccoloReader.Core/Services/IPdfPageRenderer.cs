using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

public interface IPdfPageRenderer
{
    Task<int> GetPageCountAsync(string filePath);

    // With a crop, only that part of the page is rendered, scaled so it fits
    // the target box (the output has the crop's aspect ratio, not the page's).
    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx, PageCrop? crop = null);

    // Renders only part of a page, for the sharp view while zoomed in. The
    // region is normalized (0-1) to the page as displayed, i.e. to the
    // cropped part when a crop is given. The output is exactly
    // outputWidthPx x outputHeightPx, so memory does not grow with zoom.
    Task<byte[]> RenderPageRegionAsync(
        string filePath,
        int pageIndex,
        double regionX,
        double regionY,
        double regionWidth,
        double regionHeight,
        int outputWidthPx,
        int outputHeightPx,
        PageCrop? crop,
        CancellationToken cancellationToken);
}
