using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

public interface IPdfPageRenderer
{
    Task<int> GetPageCountAsync(string filePath);

    // With a crop, only that part of the page is rendered, scaled so it fits
    // the target box (the output has the crop's aspect ratio, not the page's).
    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx, PageCrop? crop = null);
}
