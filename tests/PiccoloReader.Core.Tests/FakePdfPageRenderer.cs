using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakePdfPageRenderer : IPdfPageRenderer
{
    public int PageCountToReturn { get; set; } = 3;

    public int GetPageCountCallCount { get; private set; }

    public List<int> RenderedPageIndexes { get; } = new();

    public Task<int> GetPageCountAsync(string filePath)
    {
        GetPageCountCallCount++;
        return Task.FromResult(PageCountToReturn);
    }

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx)
    {
        RenderedPageIndexes.Add(pageIndex);
        return Task.FromResult(new byte[] { (byte)pageIndex });
    }
}
