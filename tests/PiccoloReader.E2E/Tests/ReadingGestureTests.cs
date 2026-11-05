using System.Drawing;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Paged reading gestures: pinch zoom, panning while zoomed, swipes in both directions and axes.</summary>
[TestFixture]
public class ReadingGestureTests : E2ETestBase
{
    [SetUp]
    public void OpenSample() => LibraryScreen.OpenSheet(SheetTitle);

    private static Point At(Rectangle area, double fx, double fy) =>
        new(area.X + (int)(area.Width * fx), area.Y + (int)(area.Height * fy));

    private static void ZoomIn()
    {
        var area = ViewerScreen.PageArea;
        Gestures.Pinch(At(area, 0.5, 0.5), startHalfSpan: 60, endHalfSpan: 330, durationMs: 900);
        Thread.Sleep(600);
    }

    private static void ZoomOutToFit()
    {
        var area = ViewerScreen.PageArea;

        // Twice: the first pinch may stop above the 1x floor.
        for (var i = 0; i < 2; i++)
        {
            Gestures.Pinch(At(area, 0.5, 0.5), startHalfSpan: 330, endHalfSpan: 40, durationMs: 900);
            Thread.Sleep(600);
        }
    }

    [Test]
    public void PinchZoom_OneFingerDragPansInsteadOfTurningThePage_AndSideTapTurnsAfterZoomOut()
    {
        ViewerScreen.RevealChrome();
        var area = ViewerScreen.PageArea;

        ZoomIn();

        // The same long horizontal swipe that turns the page at 1x (see ViewerTests) only pans now.
        Gestures.Drag(At(area, 0.7, 0.5), At(area, 0.3, 0.5), 500);
        Thread.Sleep(1000);
        Assert.That(ViewerScreen.Indicator.Current, Is.EqualTo(1), "a drag while zoomed in pans and must not turn the page");

        ZoomOutToFit();
        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(2);
    }

    [Test]
    public void SwipeRight_GoesBackOnePage()
    {
        ViewerScreen.RevealChrome();
        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(2);

        var area = ViewerScreen.PageArea;
        Gestures.Drag(At(area, 0.15, 0.5), At(area, 0.85, 0.5), 400);

        ViewerScreen.WaitForPage(1);
    }

    [Test]
    public void VerticalPaged_SwipeUpAndDown_TurnPages()
    {
        ViewerScreen.RevealChrome();
        ViewerScreen.CycleReadingMode();
        Ui.Poll(() => AppSession.ReadStringPreference("ReadingMode") == "VerticalPaged", 10, "vertical paged mode saved");
        var area = ViewerScreen.PageArea;

        // Swipe up = next page.
        Gestures.Drag(At(area, 0.5, 0.8), At(area, 0.5, 0.2), 400);
        ViewerScreen.WaitForPage(2);

        // Swipe down = previous page.
        Gestures.Drag(At(area, 0.5, 0.2), At(area, 0.5, 0.8), 400);
        ViewerScreen.WaitForPage(1);
    }

    [Test, Category("Gesture")]
    public void TwoFingerPan_ThenOneFingerContinues_DoesNotTurnThePage()
    {
        ViewerScreen.RevealChrome();
        var area = ViewerScreen.PageArea;
        ZoomIn();
        var before = ((OpenQA.Selenium.ITakesScreenshot)AppSession.Driver).GetScreenshot().AsBase64EncodedString;

        // Two fingers drag left, one lifts, the other keeps dragging.
        Gestures.TwoFingerDragThenOneFinger(At(area, 0.5, 0.5), halfSpan: 120, dx: -120, dy: 0);
        Thread.Sleep(1000);

        Assert.That(ViewerScreen.ChromeVisible || Ui.Exists(ViewerScreen.PageContainer), Is.True, "the viewer is still alive");
        if (ViewerScreen.ChromeVisible)
        {
            Assert.That(ViewerScreen.Indicator.Current, Is.EqualTo(1), "panning must not turn the page");
        }

        // Best effort: there is no exposed translation state, so only warn when the picture did not change.
        var after = ((OpenQA.Selenium.ITakesScreenshot)AppSession.Driver).GetScreenshot().AsBase64EncodedString;
        if (before == after)
        {
            Assert.Warn("The screen looks identical after the pan; the translation could not be verified.");
        }
    }
}
