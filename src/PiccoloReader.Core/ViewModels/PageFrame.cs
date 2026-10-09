namespace PiccoloReader.Core.ViewModels;

// Annotations are stored normalized (0-1) to the page itself. The
// single-page viewer's PageContainer fills the whole reading area with the
// page image letterboxed (AspectFit) inside it, while the continuous list
// draws on an area that is exactly the page. A PageFrame is the page's
// rectangle within a container, in container-normalized coordinates, and
// converts between container and page space (page space is also what legacy
// container-normalized annotations are migrated into).
public readonly record struct PageFrame(double X, double Y, double Width, double Height)
{
    public static PageFrame Full { get; } = new(0, 0, 1, 1);

    // Page (height / width = aspectRatio) fitted and centered in a container.
    public static PageFrame Fit(double containerWidth, double containerHeight, double aspectRatio)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || aspectRatio <= 0)
        {
            return Full;
        }

        var pageWidth = Math.Min(containerWidth, containerHeight / aspectRatio);
        var pageHeight = pageWidth * aspectRatio;
        return new PageFrame(
            (containerWidth - pageWidth) / 2 / containerWidth,
            (containerHeight - pageHeight) / 2 / containerHeight,
            pageWidth / containerWidth,
            pageHeight / containerHeight);
    }

    public double ToPageX(double containerX) => (containerX - X) / Width;

    public double ToPageY(double containerY) => (containerY - Y) / Height;

    public double ToContainerX(double pageX) => X + pageX * Width;

    public double ToContainerY(double pageY) => Y + pageY * Height;
}
