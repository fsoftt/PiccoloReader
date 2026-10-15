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

    // Draws only the requested part of a page, so the cost and memory are
    // those of one screen-sized bitmap however far the user has zoomed in.
    // PdfRenderer lets the caller pass a transform from page units (points)
    // to bitmap pixels, which is what makes rendering just a window possible
    // without ever allocating the zoomed-in page.
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
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        using var page = renderer.OpenPage(pageIndex);

        cancellationToken.ThrowIfCancellationRequested();

        var shown = crop is { IsFull: false, IsValid: true } ? crop.Value : PageCrop.Full;

        // The region is relative to the displayed (cropped) page: map it to
        // page units, then scale it up to fill the output bitmap.
        var left = (shown.Left + regionX * shown.Width) * page.Width;
        var top = (shown.Top + regionY * shown.Height) * page.Height;
        var regionWidthPts = regionWidth * shown.Width * page.Width;
        var regionHeightPts = regionHeight * shown.Height * page.Height;
        if (regionWidthPts <= 0 || regionHeightPts <= 0)
        {
            throw new ArgumentException("Empty region.");
        }

        var width = Math.Max(1, outputWidthPx);
        var height = Math.Max(1, outputHeightPx);
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
        bitmap.EraseColor(global::Android.Graphics.Color.White);

        using var matrix = new Matrix();
        matrix.SetTranslate((float)-left, (float)-top);
        matrix.PostScale((float)(width / regionWidthPts), (float)(height / regionHeightPts));
        page.Render(bitmap, null, matrix, PdfRenderMode.ForDisplay);

        cancellationToken.ThrowIfCancellationRequested();

        // JPEG, not PNG: the bitmap is opaque (white background), and PNG at
        // q100 of a screen-sized bitmap is several times slower to encode,
        // which would delay the swap to the sharp image. q92 keeps text
        // edges clean at the density they are shown.
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 92, stream);
        return stream.ToArray();
    }, cancellationToken);

    private static byte[] Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    }
}
