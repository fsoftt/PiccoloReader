using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Reading: opening, paging, page pill, reading modes, bookmarks, chrome toggle.</summary>
[TestFixture]
public class ViewerTests : E2ETestBase
{
    [SetUp]
    public void OpenSample() => LibraryScreen.OpenSheet(SheetTitle);

    [Test]
    public void OpenSheet_ShowsFirstPageOfThree()
    {
        ViewerScreen.RevealChrome();

        Assert.That(ViewerScreen.Indicator, Is.EqualTo((1, SheetPages)));
        Assert.That(Ui.TextOf("ViewerTitleLabel"), Is.EqualTo(SheetTitle));
    }

    [Test]
    public void SideTaps_TurnPagesForwardAndBack()
    {
        ViewerScreen.RevealChrome();

        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(2);
        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(3);

        ViewerScreen.TapNextPageZone();
        Thread.Sleep(800);
        Assert.That(ViewerScreen.Indicator.Current, Is.EqualTo(3), "cannot go past the last page");

        ViewerScreen.TapPreviousPageZone();
        ViewerScreen.WaitForPage(2);
    }

    [Test]
    public void SwipeLeft_TurnsToTheNextPage()
    {
        ViewerScreen.RevealChrome();
        var area = ViewerScreen.PageArea;
        var y = area.Y + area.Height / 2;

        Gestures.Drag(
            new System.Drawing.Point(area.X + (int)(area.Width * 0.85), y),
            new System.Drawing.Point(area.X + (int)(area.Width * 0.15), y),
            400);

        ViewerScreen.WaitForPage(2);
    }

    [Test]
    public void PagePill_JumpsToTheTypedPage()
    {
        ViewerScreen.RevealChrome();

        ViewerScreen.GoToPage(3);

        ViewerScreen.WaitForPage(3);
    }

    [Test]
    public void ToolbarToggles_WhenTappingThePage()
    {
        Assert.That(ViewerScreen.ChromeVisible, Is.False, "the chrome starts hidden");

        ViewerScreen.ToggleChromeByTap();
        Ui.Find(ViewerScreen.PageIndicator, 10);
        Assert.That(Ui.Exists(ViewerScreen.ModeButton), Is.True);

        ViewerScreen.ToggleChromeByTap();
        Ui.WaitGone(ViewerScreen.PageIndicator, 10);
        Assert.That(Ui.Exists(ViewerScreen.ModeButton), Is.False);
    }

    [Test]
    public void ModeButton_CyclesThroughThreeModes()
    {
        ViewerScreen.RevealChrome();
        Assert.That(ViewerScreen.IsContinuous, Is.False, "starts horizontal");
        Assert.That(AppSession.ReadStringPreference("ReadingMode"), Is.Null.Or.EqualTo("Horizontal"), "nothing saved yet or the default");

        // Horizontal -> vertical paged: still the single-page surface, but turning is top/bottom.
        ViewerScreen.CycleReadingMode();
        Ui.Poll(() => AppSession.ReadStringPreference("ReadingMode") == "VerticalPaged", 10, "vertical paged mode saved");
        Assert.That(ViewerScreen.IsContinuous, Is.False);
        ViewerScreen.TapBottomZone();
        ViewerScreen.WaitForPage(2);

        // -> continuous list.
        ViewerScreen.CycleReadingMode();
        Ui.Poll(() => AppSession.ReadStringPreference("ReadingMode") == "VerticalContinuous", 10, "continuous mode saved");
        Ui.Find(ViewerScreen.ContinuousHost, 10);
        Assert.That(Ui.Exists(ViewerScreen.PageContainer), Is.False, "continuous mode replaces the single page");

        // -> back to horizontal.
        ViewerScreen.CycleReadingMode();
        Ui.Poll(() => AppSession.ReadStringPreference("ReadingMode") == "Horizontal", 10, "horizontal mode saved");
        Ui.Find(ViewerScreen.PageContainer, 10);
        Assert.That(ViewerScreen.IsContinuous, Is.False);
    }

    [Test]
    public void ReadingMode_PersistsAcrossRelaunch()
    {
        ViewerScreen.RevealChrome();
        ViewerScreen.CycleReadingMode();
        ViewerScreen.CycleReadingMode(); // continuous
        Ui.Find(ViewerScreen.ContinuousHost, 10);
        Ui.Poll(() => AppSession.ReadStringPreference("ReadingMode") == "VerticalContinuous", 10, "mode saved");

        AppSession.Relaunch();
        LibraryScreen.OpenSheet(SheetTitle);

        Assert.That(ViewerScreen.IsContinuous, Is.True, "the saved reading mode is restored on the next launch");
    }

    [Test]
    public void Bookmark_AddedFromMenu_IsStoredAndListed()
    {
        ViewerScreen.RevealChrome();

        ViewerScreen.AddBookmarkForCurrentPage();

        var bookmarks = AppDb.Bookmarks();
        Assert.That(bookmarks, Has.Count.EqualTo(1));
        Assert.That(bookmarks[0].PageIndex, Is.EqualTo(0));

        // The menu now lists it ("Page 1"); jumping from another page returns to it.
        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(2);
        Ui.Tap(ViewerScreen.BookmarkButton);
        Ui.TapText(string.Format(AppStrings.BookmarkPageLabelFormat, 1));
        ViewerScreen.WaitForPage(1);
    }
}
