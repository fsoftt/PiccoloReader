using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakePdfPageRenderer : IPdfPageRenderer
{
    public int PageCountToReturn { get; set; } = 3;

    public int GetPageCountCallCount { get; private set; }

    public List<int> RenderedPageIndexes { get; } = new();

    // When set, renders wait on this before completing - lets tests hold a
    // render "in flight" while navigating further.
    public TaskCompletionSource? RenderGate { get; set; }

    public Task<int> GetPageCountAsync(string filePath)
    {
        GetPageCountCallCount++;
        return Task.FromResult(PageCountToReturn);
    }

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx)
    {
        RenderedPageIndexes.Add(pageIndex);
        return RenderGate is null
            ? Task.FromResult(new byte[] { (byte)pageIndex })
            : RenderAfterGateAsync(RenderGate, pageIndex);
    }

    private static async Task<byte[]> RenderAfterGateAsync(TaskCompletionSource gate, int pageIndex)
    {
        await gate.Task;
        return new byte[] { (byte)pageIndex };
    }
}
