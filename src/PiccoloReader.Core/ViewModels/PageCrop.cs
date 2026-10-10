namespace PiccoloReader.Core.ViewModels;

// Which handle of the crop rectangle a drag moves.
public enum CropHandle
{
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

// The visible part of a page, as a rectangle normalized (0-1) to the FULL
// page. Annotations stay stored normalized to the full page; the crop only
// decides which part of it is shown, so these helpers map between full-page
// and cropped-page coordinates (cropped space is 0-1 over the visible part).
public readonly record struct PageCrop(double Left, double Top, double Right, double Bottom)
{
    // Smallest side a crop may have, as a fraction of the page's side.
    public const double MinSize = 0.2;

    public static PageCrop Full { get; } = new(0, 0, 1, 1);

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool IsFull => Left <= 0 && Top <= 0 && Right >= 1 && Bottom >= 1;

    // Whether the values describe a rectangle inside the page that respects
    // the minimum size (guards values read back from storage).
    public bool IsValid =>
        Left >= 0 && Top >= 0 && Right <= 1 && Bottom <= 1 &&
        Width >= MinSize - 1e-9 && Height >= MinSize - 1e-9;

    // The crop itself when valid, otherwise the full page.
    public PageCrop OrFull() => IsValid ? this : Full;

    public bool Contains(double pageX, double pageY) =>
        pageX >= Left && pageX <= Right && pageY >= Top && pageY <= Bottom;

    public double ToCroppedX(double pageX) => (pageX - Left) / Width;

    public double ToCroppedY(double pageY) => (pageY - Top) / Height;

    public double ToPageX(double croppedX) => Left + croppedX * Width;

    public double ToPageY(double croppedY) => Top + croppedY * Height;

    // Height / width of the visible part, given the full page's.
    public double CroppedAspectRatio(double fullAspectRatio) => fullAspectRatio * Height / Width;

    // Inverse of CroppedAspectRatio.
    public double FullAspectRatio(double croppedAspectRatio) => croppedAspectRatio * Width / Height;

    // The crop after dragging a handle by (dx, dy), both fractions of the
    // full page. The rectangle stays inside the page and never gets smaller
    // than minSize on a side (the dragged edge stops, it does not push the
    // opposite one).
    public PageCrop Drag(CropHandle handle, double dx, double dy, double minSize = MinSize)
    {
        var left = Left;
        var top = Top;
        var right = Right;
        var bottom = Bottom;

        if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
        {
            left = Math.Clamp(Left + dx, 0, Right - minSize);
        }

        if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
        {
            right = Math.Clamp(Right + dx, Left + minSize, 1);
        }

        if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
        {
            top = Math.Clamp(Top + dy, 0, Bottom - minSize);
        }

        if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
        {
            bottom = Math.Clamp(Bottom + dy, Top + minSize, 1);
        }

        return new PageCrop(left, top, right, bottom);
    }
}
