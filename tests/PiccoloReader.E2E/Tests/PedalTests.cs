using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Hardware page-turn keys (what a Bluetooth pedal or keyboard sends), injected with "adb shell input keyevent".</summary>
[TestFixture]
public class PedalTests : E2ETestBase
{
    [SetUp]
    public void OpenSample()
    {
        LibraryScreen.OpenSheet(SheetTitle);
        ViewerScreen.RevealChrome();
    }

    private static void Key(string keyCode) => Adb.Shell($"input keyevent {keyCode}");

    [Test]
    public void PageDownAndPageUp_TurnPagesForwardAndBack()
    {
        Key("KEYCODE_PAGE_DOWN");
        ViewerScreen.WaitForPage(2);
        Key("KEYCODE_PAGE_DOWN");
        ViewerScreen.WaitForPage(3);

        Key("KEYCODE_PAGE_UP");
        ViewerScreen.WaitForPage(2);
    }

    [Test]
    public void Space_TurnsForward()
    {
        Key("KEYCODE_SPACE");
        ViewerScreen.WaitForPage(2);
    }

    [Test]
    public void PageUpOnFirstPage_StaysOnFirstPage()
    {
        Key("KEYCODE_PAGE_UP");
        Thread.Sleep(1000);

        Assert.That(ViewerScreen.Indicator.Current, Is.EqualTo(1));
    }
}
