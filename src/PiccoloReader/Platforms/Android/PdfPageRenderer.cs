using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Platforms.Android;

public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        return renderer.PageCount;
    });

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        using var page = renderer.OpenPage(pageIndex);

        var scale = Math.Min((float)targetWidthPx / page.Width, (float)targetHeightPx / page.Height);
        var bitmapWidth = Math.Max(1, (int)(page.Width * scale));
        var bitmapHeight = Math.Max(1, (int)(page.Height * scale));

        using var bitmap = Bitmap.CreateBitmap(bitmapWidth, bitmapHeight, Bitmap.Config.Argb8888!);
        bitmap.EraseColor(global::Android.Graphics.Color.White);
        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    });
}
