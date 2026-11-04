using System.Drawing;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Crop page: opened from the More menu, a handle is dragged, the crop is applied and stored.</summary>
[TestFixture]
public class CropTests : E2ETestBase
{
    [SetUp]
    public void OpenSample()
    {
        LibraryScreen.OpenSheet(SheetTitle);
        ViewerScreen.RevealChrome();
    }

    private static void OpenCropAndDragTopLeftHandle()
    {
        Ui.Tap(ViewerScreen.MoreButton);
        Ui.TapText(AppStrings.CropPage);
        Ui.Find("CropOverlay", 20);
        Ui.Find("CropHandle_TopLeft", 20);

        var handle = Ui.CenterOf("CropHandle_TopLeft");
        Gestures.Drag(handle, new Point(handle.X + 160, handle.Y + 220), 800);
        Thread.Sleep(500);
    }

    [Test]
    public void DraggingTheTopLeftHandle_AndApplying_StoresTheCrop()
    {
        OpenCropAndDragTopLeftHandle();

        Ui.Tap("CropApplyButton");
        Ui.WaitGone("CropOverlay", 10);

        Ui.Poll(() => AppDb.PageCrops().Count == 1, 20, "the page crop to be stored");
        var crop = AppDb.PageCrops().Single();
        Assert.That(crop.PageIndex, Is.EqualTo(0));
        Assert.That(crop.Left, Is.GreaterThan(0.05), "the left edge moved in");
        Assert.That(crop.Top, Is.GreaterThan(0.05), "the top edge moved down");
        Assert.That(crop.Right, Is.EqualTo(1).Within(0.001), "untouched edges stay at the page border");
        Assert.That(crop.Bottom, Is.EqualTo(1).Within(0.001));
    }

    [Test]
    public void Cancel_StoresNothing()
    {
        OpenCropAndDragTopLeftHandle();

        Ui.Tap("CropCancelButton");
        Ui.WaitGone("CropOverlay", 10);
        Thread.Sleep(1500);

        Assert.That(AppDb.PageCrops(), Is.Empty);
    }
}
