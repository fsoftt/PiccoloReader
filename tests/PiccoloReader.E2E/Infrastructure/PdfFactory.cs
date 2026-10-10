using System.Globalization;
using System.Text;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>
/// Hand-written minimal PDF generator (no library): N pages of arbitrary size, each with a border
/// and a large "Page n" label so every page renders as visibly non-blank content.
/// </summary>
public static class PdfFactory
{
    public const double PortraitWidth = 595;
    public const double PortraitHeight = 842;

    private static readonly Dictionary<string, string> Cache = new();

    /// <summary>A generated PDF, built once per run per shape (pages x orientation).</summary>
    public static string CreateCached(int pages, bool landscape)
    {
        var key = $"{pages}-{landscape}";
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var path))
            {
                path = Path.Combine(Path.GetTempPath(), "piccolo-e2e", $"sample-{key}.pdf");
                var size = landscape ? (PortraitHeight, PortraitWidth) : (PortraitWidth, PortraitHeight);
                Create(path, Enumerable.Repeat(size, pages).ToArray());
                Cache[key] = path;
            }

            return path;
        }
    }

    public static string CreatePortrait(string path, int pages) =>
        Create(path, Enumerable.Repeat((PortraitWidth, PortraitHeight), pages).ToArray());

    public static string Create(string path, params (double Width, double Height)[] pages)
    {
        var objects = new List<string>();

        // 1 = catalog, 2 = pages tree, 3 = font; then per page: page object + content stream.
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", pages.Select((_, i) => $"{4 + i * 2} 0 R"));
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pages.Length} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (var i = 0; i < pages.Length; i++)
        {
            var (w, h) = pages[i];
            var contentId = 5 + i * 2;
            objects.Add(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));

            var stream = string.Create(CultureInfo.InvariantCulture,
                $"4 w 20 20 {w - 40} {h - 40} re S\nBT /F1 96 Tf {w / 2 - 140} {h / 2 - 30} Td (Page {i + 1}) Tj ET\n");
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        }

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Pure ASCII, so Latin1 byte count == character count (the offsets above rely on this).
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
        return path;
    }
}
