using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services.Stylus;

public static class ContinuousStylusGeometry
{
    /// <summary>
    /// Maps a touch point in the continuous list's own (unzoomed) space to the
    /// page-normalized point on one of its items. The item's page area starts
    /// at (itemLeft, itemTop) and is itemWidth x itemHeight, the same box
    /// ContinuousPageView paints the page into. The result can lie outside
    /// 0-1; stroke clipping handles that. Null when the box is empty.
    /// </summary>
    public static StrokePoint? ToPagePoint(
        double x, double y, double itemLeft, double itemTop, double itemWidth, double itemHeight, double fullAspectRatio, PageCrop crop)
    {
        if (itemWidth <= 0 || itemHeight <= 0)
        {
            return null;
        }

        var frame = PageFrame.Fit(itemWidth, itemHeight, fullAspectRatio, crop);
        return new StrokePoint(
            frame.ToPageX((x - itemLeft) / itemWidth),
            frame.ToPageY((y - itemTop) / itemHeight));
    }
}
