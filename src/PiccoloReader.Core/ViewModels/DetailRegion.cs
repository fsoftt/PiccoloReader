namespace PiccoloReader.Core.ViewModels;

// A plain rectangle (x, y, width, height) in some local coordinate system.
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public RectD? Intersect(RectD other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right - left > 0 && bottom - top > 0 ? new RectD(left, top, right - left, bottom - top) : null;
    }
}

// The part of a displayed page image that is on screen while zoomed in, and
// how big a bitmap it needs to be sharp: a page is rendered once at screen
// width, so zooming would only magnify those pixels. A "detail" render
// re-draws just the visible part at the on-screen resolution instead, so its
// size is bounded by the screen however far the user has zoomed.
//
// Image: the part of the page the user sees (the cropped page) as laid out
// in an unscaled local space. Local: that same part's piece currently inside
// the viewport, in the same space. Because a detail image can be placed in
// that local space (inside the zoomed view), it is magnified together with
// the page and lines up with it exactly.
public readonly record struct DetailRegion(
    double NormX,
    double NormY,
    double NormWidth,
    double NormHeight,
    RectD Local,
    int PixelWidth,
    int PixelHeight)
{
    // A view zoomed by `scale` around its top-left corner and shifted by
    // (translationX, translationY) shows this part of its own unscaled
    // width x height space.
    public static RectD VisibleLocalRect(double width, double height, double scale, double translationX, double translationY)
    {
        if (scale <= 0)
        {
            return new RectD(0, 0, width, height);
        }

        return new RectD(-translationX / scale, -translationY / scale, width / scale, height / scale);
    }

    // The image rectangle of an AspectFit image (height / width =
    // aspectRatio) centered in a container.
    public static RectD AspectFit(double containerWidth, double containerHeight, double aspectRatio)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || aspectRatio <= 0)
        {
            return new RectD(0, 0, containerWidth, containerHeight);
        }

        var width = Math.Min(containerWidth, containerHeight / aspectRatio);
        var height = width * aspectRatio;
        return new RectD((containerWidth - width) / 2, (containerHeight - height) / 2, width, height);
    }

    // `visible` is the viewport and `image` the displayed page image, both in
    // the same local space; `scale` magnifies that space on screen and
    // `pixelsPerUnit` converts screen units to physical pixels (the display
    // density). The bitmap is capped to maxPixelSide on its longer side.
    // Null when none of the image is visible.
    public static DetailRegion? Compute(RectD visible, RectD image, double scale, double pixelsPerUnit, int maxPixelSide)
    {
        if (image.IsEmpty || visible.IsEmpty || scale <= 0 || pixelsPerUnit <= 0 || maxPixelSide <= 0)
        {
            return null;
        }

        if (visible.Intersect(image) is not { } local)
        {
            return null;
        }

        var pixelWidth = local.Width * scale * pixelsPerUnit;
        var pixelHeight = local.Height * scale * pixelsPerUnit;
        var shrink = Math.Min(1, maxPixelSide / Math.Max(pixelWidth, pixelHeight));

        return new DetailRegion(
            (local.X - image.X) / image.Width,
            (local.Y - image.Y) / image.Height,
            local.Width / image.Width,
            local.Height / image.Height,
            local,
            Math.Max(1, (int)Math.Round(pixelWidth * shrink)),
            Math.Max(1, (int)Math.Round(pixelHeight * shrink)));
    }
}
