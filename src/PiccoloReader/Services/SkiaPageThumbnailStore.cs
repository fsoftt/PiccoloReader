using PiccoloReader.Core.Services;
using SkiaSharp;

namespace PiccoloReader.Services;

public class SkiaPageThumbnailStore : IPageThumbnailStore
{
    private const int LongSidePx = 800;

    private readonly string _directory;

    public SkiaPageThumbnailStore(string directory)
    {
        _directory = directory;
    }

    private string PathFor(int sheetId, int pageIndex) => Path.Combine(_directory, $"{sheetId}_p{pageIndex}.png");

    public byte[]? TryLoad(int sheetId, int pageIndex)
    {
        try
        {
            var path = PathFor(sheetId, pageIndex);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail load failed: {ex.Message}");
            return null;
        }
    }

    public Task SaveAsync(int sheetId, int pageIndex, byte[] pageImageBytes) => Task.Run(() =>
    {
        try
        {
            using var source = SKBitmap.Decode(pageImageBytes);
            if (source is null || source.Width <= 0 || source.Height <= 0)
            {
                return;
            }

            var scale = Math.Min(1.0, (double)LongSidePx / Math.Max(source.Width, source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var resized = scale < 1.0
                ? source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                : source.Copy();
            if (resized is null)
            {
                return;
            }

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            Directory.CreateDirectory(_directory);

            // Write then move so a reader never sees a half-written file.
            var path = PathFor(sheetId, pageIndex);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, data.ToArray());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail save failed: {ex.Message}");
        }
    });

    public void DeleteForSheet(int sheetId)
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, $"{sheetId}_p*"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail delete failed: {ex.Message}");
        }
    }
}
