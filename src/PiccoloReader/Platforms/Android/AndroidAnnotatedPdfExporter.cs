using Android.Graphics;
using Android.OS;
using PiccoloReader.Core.Services;
using PiccoloReader.Views;
using SkiaSharp;

namespace PiccoloReader.Platforms.Android;

// Rasterizes each page with PdfRenderer (JPEG, ~200 DPI) and rebuilds the PDF
// with Skia, drawing the annotations on top as vector paths and text. One page
// is in memory at a time. Every page is the FULL original page: a reading crop
// is only a view aid.
public class AndroidAnnotatedPdfExporter : IAnnotatedPdfExporter
{
    private const float TargetDpi = 200f;
    private const int MaxLongSidePx = 2400;
    private const int JpegQuality = 85;

    private readonly AnnotationService _annotations;

    public AndroidAnnotatedPdfExporter(AnnotationService annotations) => _annotations = annotations;

    public async Task ExportAsync(
        string sourcePdfPath,
        int sheetId,
        string destinationPath,
        (double Width, double Height) legacyReferenceArea,
        CancellationToken cancellationToken = default)
    {
        using var typeface = await LoadBravuraAsync();
        var painter = new AnnotationPainter { Typeface = typeface };

        await Task.Run(async () =>
        {
            using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(sourcePdfPath), ParcelFileMode.ReadOnly);
            using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
            using var output = File.Create(destinationPath);
            using var document = SKDocument.CreatePdf(output);

            for (var pageIndex = 0; pageIndex < renderer.PageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var page = renderer.OpenPage(pageIndex);
                float widthPts = page.Width;
                float heightPts = page.Height;

                var annotations = await _annotations.GetPageAnnotationsAsync(
                    sheetId, pageIndex, heightPts / widthPts, legacyReferenceArea.Width, legacyReferenceArea.Height, persist: false);

                var jpeg = RenderJpeg(page, widthPts, heightPts);
                using var image = SKImage.FromEncodedData(jpeg);
                jpeg = null;

                var canvas = document.BeginPage(widthPts, heightPts);
                canvas.DrawImage(image, new SKRect(0, 0, widthPts, heightPts));
                painter.DrawAnnotations(canvas, widthPts, heightPts, annotations);
                document.EndPage();
            }

            document.Close();
        }, cancellationToken);
    }

    private static byte[] RenderJpeg(global::Android.Graphics.Pdf.PdfRenderer.Page page, float widthPts, float heightPts)
    {
        var scale = Math.Min(TargetDpi / 72f, MaxLongSidePx / Math.Max(widthPts, heightPts));
        var width = Math.Max(1, (int)Math.Round(widthPts * scale));
        var height = Math.Max(1, (int)Math.Round(heightPts * scale));

        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
        try
        {
            bitmap.EraseColor(global::Android.Graphics.Color.White);
            page.Render(bitmap, null, null, global::Android.Graphics.Pdf.PdfRenderMode.ForPrint);
            using var stream = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg!, JpegQuality, stream);
            return stream.ToArray();
        }
        finally
        {
            bitmap.Recycle();
        }
    }

    private static async Task<SKTypeface> LoadBravuraAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("Bravura.otf");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        memory.Position = 0;
        return SKTypeface.FromStream(memory);
    }
}
