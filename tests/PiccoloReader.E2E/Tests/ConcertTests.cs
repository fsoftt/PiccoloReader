using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>
/// Concert mode (on by default): "next" on the last page opens the next score of the list the
/// viewer was opened from, "previous" on the first page goes back to the end of the previous one.
/// </summary>
[TestFixture]
public class ConcertTests : E2ETestBase
{
    protected override IReadOnlyList<SeedSheet> Sheets => new[] { new SeedSheet("Concert A", 2), new SeedSheet("Concert B", 2) };

    private static string ViewerTitle => Ui.TextOf("ViewerTitleLabel");

    /// <summary>The title lives in the chrome: bring it up if the sheet switch hid it.</summary>
    private static bool TitleIs(string title)
    {
        ViewerScreen.RevealChrome();
        return ViewerTitle == title;
    }

    [Test]
    public void NextOnTheLastPage_OpensTheNextSheet_AndPreviousComesBack()
    {
        var order = LibraryScreen.SheetTitles();
        Assert.That(order, Has.Count.EqualTo(2));
        var first = order[0];
        var second = order[1];

        LibraryScreen.OpenSheet(first);
        ViewerScreen.RevealChrome();
        Assert.That(ViewerTitle, Is.EqualTo(first));

        ViewerScreen.TapNextPageZone();
        ViewerScreen.WaitForPage(2);

        // Last page of the first sheet: next crosses into the second sheet, on its first page.
        ViewerScreen.TapNextPageZone();
        Ui.Poll(() => TitleIs(second), 20, "the viewer to switch to the second sheet");
        ViewerScreen.WaitForPage(1);

        // First page of the second sheet: previous goes back onto the last page of the first.
        ViewerScreen.TapPreviousPageZone();
        Ui.Poll(() => TitleIs(first), 20, "the viewer to switch back to the first sheet");
        ViewerScreen.WaitForPage(2);
    }
}
