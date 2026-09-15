namespace PiccoloReader.Core.Services;

public interface IPdfPageRenderer
{
    Task<int> GetPageCountAsync(string filePath);

    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx);
}
