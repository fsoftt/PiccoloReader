using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Platforms.Android;

public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        return renderer.PageCount;
    });

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx, PageCrop? crop = null) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        using var page = renderer.OpenPage(pageIndex);

        var region = crop is { IsFull: false, IsValid: true } ? crop.Value : PageCrop.Full;

        // The output is just the cropped part, scaled to fit the target.
        var regionWidth = page.Width * region.Width;
        var regionHeight = page.Height * region.Height;
        var scale = Math.Min(targetWidthPx / regionWidth, targetHeightPx / regionHeight);

        if (region.IsFull)
        {
            var width = Math.Max(1, (int)(page.Width * scale));
            var height = Math.Max(1, (int)(page.Height * scale));
            using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
            bitmap.EraseColor(global::Android.Graphics.Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
            return Encode(bitmap);
        }

        // PdfRenderer can only render the whole page into a bitmap, so the
        // page is rendered at the scale that makes the crop fill the target
        // (capped, to bound memory) and the crop is cut out of it.
        const float MaxSidePx = 4096f;
        scale = Math.Min(scale, Math.Min(MaxSidePx / page.Width, MaxSidePx / page.Height));
        var fullWidth = Math.Max(1, (int)Math.Round(page.Width * scale));
        var fullHeight = Math.Max(1, (int)Math.Round(page.Height * scale));

        using var full = Bitmap.CreateBitmap(fullWidth, fullHeight, Bitmap.Config.Argb8888!);
        full.EraseColor(global::Android.Graphics.Color.White);
        page.Render(full, null, null, PdfRenderMode.ForDisplay);

        var left = Math.Clamp((int)Math.Round(region.Left * fullWidth), 0, fullWidth - 1);
        var top = Math.Clamp((int)Math.Round(region.Top * fullHeight), 0, fullHeight - 1);
        var cropWidth = Math.Clamp((int)Math.Round(region.Width * fullWidth), 1, fullWidth - left);
        var cropHeight = Math.Clamp((int)Math.Round(region.Height * fullHeight), 1, fullHeight - top);
        using var cropped = Bitmap.CreateBitmap(full, left, top, cropWidth, cropHeight);
        return Encode(cropped);
    });

    private static byte[] Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    }
}
