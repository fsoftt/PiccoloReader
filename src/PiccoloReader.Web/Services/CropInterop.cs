using Microsoft.JSInterop;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Web.Services;

public static class CropInterop
{
    // Called synchronously from viewer.js while a crop handle is dragged: Core's
    // PageCrop.Drag keeps the rectangle inside the page and above the minimum size.
    [JSInvokable]
    public static double[] DragCrop(string handle, double left, double top, double right, double bottom, double dx, double dy)
    {
        var c = new PageCrop(left, top, right, bottom).Drag(Enum.Parse<CropHandle>(handle), dx, dy);
        return [c.Left, c.Top, c.Right, c.Bottom];
    }
}
