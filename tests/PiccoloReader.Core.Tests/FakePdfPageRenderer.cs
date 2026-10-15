using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class FakePdfPageRenderer : IPdfPageRenderer
{
    public int PageCountToReturn { get; set; } = 3;

    public int GetPageCountCallCount { get; private set; }

    public List<int> RenderedPageIndexes { get; } = new();

    // Crop passed for each render, parallel to RenderedPageIndexes.
    public List<PageCrop?> RenderedCrops { get; } = new();

    // When set, renders wait on this before completing - lets tests hold a
    // render "in flight" while navigating further.
    public TaskCompletionSource? RenderGate { get; set; }

    public Task<int> GetPageCountAsync(string filePath)
    {
        GetPageCountCallCount++;
        return Task.FromResult(PageCountToReturn);
    }

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx, PageCrop? crop = null)
    {
        RenderedPageIndexes.Add(pageIndex);
        RenderedCrops.Add(crop);
        return RenderGate is null
            ? Task.FromResult(new byte[] { (byte)pageIndex })
            : RenderAfterGateAsync(RenderGate, pageIndex);
    }

    public List<(int PageIndex, double X, double Y, double Width, double Height, int PixelWidth, int PixelHeight, PageCrop? Crop)> RenderedRegions { get; } = new();

    public Task<byte[]> RenderPageRegionAsync(
        string filePath, int pageIndex, double regionX, double regionY, double regionWidth, double regionHeight,
        int outputWidthPx, int outputHeightPx, PageCrop? crop, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenderedRegions.Add((pageIndex, regionX, regionY, regionWidth, regionHeight, outputWidthPx, outputHeightPx, crop));
        return Task.FromResult(new byte[] { 1, (byte)pageIndex });
    }

    private static async Task<byte[]> RenderAfterGateAsync(TaskCompletionSource gate, int pageIndex)
    {
        await gate.Task;
        return new byte[] { (byte)pageIndex };
    }
}
