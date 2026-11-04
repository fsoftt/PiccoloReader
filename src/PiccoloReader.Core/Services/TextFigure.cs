using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

// Sizing rules of text annotations. The box is the text's own: its height is
// one line, its width follows from the measured text, so only the (UI-layer)
// measurement of text width / line height is needed from outside.
public static class TextFigure
{
    // One line's height, as a fraction of the page's height.
    public const double DefaultHeight = 0.025;
    public const double MinHeight = 0.012;
    public const double MaxHeight = 0.2;

    public const int MaxLength = 120;

    // Trimmed single-line text, or null when nothing is left.
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var single = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (single.Length > MaxLength)
        {
            single = single[..MaxLength];
        }

        return single.Length == 0 ? null : single;
    }

    // Box size as page fractions. textAspect is the text's width / line height
    // (as drawn); pageAspectRatio is the page's height / width. A line wider
    // than the page is made smaller so it fits.
    public static (double Width, double Height) Size(double textAspect, double height, double pageAspectRatio)
    {
        var aspect = pageAspectRatio > 0 ? pageAspectRatio : 1.0;
        height = Math.Clamp(height, MinHeight, MaxHeight);
        var width = height * aspect * Math.Max(textAspect, 0.1);
        if (width > 1.0)
        {
            height /= width;
            width = 1.0;
        }

        return (width, height);
    }

    // Top-left at the tapped point, moved as little as needed to stay inside the crop.
    public static (double X, double Y) Place(double tapX, double tapY, double width, double height, PageCrop crop)
    {
        var area = crop.IsValid ? crop : PageCrop.Full;
        return (
            Math.Clamp(tapX, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(tapY, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }
}
