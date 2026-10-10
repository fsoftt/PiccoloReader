using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Library page: folders, moving, search, sort, long-press, folder navigation.</summary>
[TestFixture]
public class LibraryTests : E2ETestBase
{
    private const string Alpha = "Alpha Score";
    private const string Bravo = "Bravo Score";

    protected override IReadOnlyList<SeedSheet> Sheets => new[] { new SeedSheet(Alpha), new SeedSheet(Bravo) };

    [Test]
    public void SeededSheets_AreListedWithTheirTitles()
    {
        Assert.That(LibraryScreen.SheetTitles(), Is.EquivalentTo(new[] { Alpha, Bravo }));
    }

    [Test]
    public void CreateFolder_AppearsInLibraryAndDatabase()
    {
        LibraryScreen.CreateFolder("Orchestra");

        Assert.That(LibraryScreen.FolderNames(), Does.Contain("Orchestra"));
        Assert.That(AppDb.Folders().Select(f => f.Name), Does.Contain("Orchestra"));
    }

    [Test]
    public void MoveSheetToFolder_LeavesRootAndShowsInsideFolder()
    {
        LibraryScreen.CreateFolder("Choir");

        LibraryScreen.OpenSheetMenu(Alpha);
        Ui.TapText(AppStrings.Move);
        Ui.TapText("Choir");
        LibraryScreen.WaitSheetGone(Alpha);

        var folder = AppDb.Folders().Single(f => f.Name == "Choir");
        Assert.That(AppDb.Sheets().Single(s => s.Title == Alpha).FolderId, Is.EqualTo(folder.Id));
        Assert.That(LibraryScreen.HasSheet(Bravo), Is.True, "the other sheet stays in the library root");

        LibraryScreen.OpenFolder("Choir");
        Assert.That(LibraryScreen.HasSheet(Alpha), Is.True, "the moved sheet is listed inside the folder");
        Assert.That(LibraryScreen.HasSheet(Bravo), Is.False);
    }

    [Test]
    public void OpenFolderAndBack_ReturnsToLibrary()
    {
        LibraryScreen.CreateFolder("Quartet");

        LibraryScreen.OpenFolder("Quartet");
        Assert.That(Ui.TextOf("FolderTitleLabel"), Is.EqualTo("Quartet"));

        LibraryScreen.BackFromFolder();
        Assert.That(LibraryScreen.HasSheet(Alpha), Is.True);
    }

    [Test]
    public void LongPressOnSheet_ShowsMenuWithoutOpeningTheViewer()
    {
        // Regression #84: a long press used to open the sheet when the finger lifted.
        LibraryScreen.LongPressSheet(Alpha);

        Assert.That(
            () => LibraryScreen.SheetMenuIsShown(),
            Is.True.After(10_000, 400),
            "the sheet menu (Move / Delete) is shown");
        Assert.That(ViewerScreen.IsOpen, Is.False, "the viewer must not open on a long press");

        LibraryScreen.DismissMenu();
        Thread.Sleep(1000);
        Assert.That(ViewerScreen.IsOpen, Is.False, "dismissing the menu must not open the viewer either");
        Assert.That(Ui.Exists(LibraryScreen.ImportButton), Is.True);
    }

    [Test]
    public void Search_FiltersTheSheetList()
    {
        Ui.SetText(LibraryScreen.SearchEntry, "Bravo");

        Ui.Poll(() => !LibraryScreen.HasSheet(Alpha), 10, "the search to hide the non-matching sheet");
        Assert.That(LibraryScreen.HasSheet(Bravo), Is.True);

        Ui.Tap("LibrarySearchClearButton");
        Ui.Poll(() => LibraryScreen.HasSheet(Alpha) && LibraryScreen.HasSheet(Bravo), 10, "clearing search to show both sheets");
    }

    [Test]
    public void Sort_ByNameDescendingReversesTheOrder()
    {
        Ui.Tap(LibraryScreen.SortButton);
        Ui.TapText(AppStrings.SortNameAscending);
        Ui.Poll(() => LibraryScreen.SheetTitles().SequenceEqual(new[] { Alpha, Bravo }), 10, "A-Z order");

        Ui.Tap(LibraryScreen.SortButton);
        Ui.TapText(AppStrings.SortNameDescending);
        Ui.Poll(() => LibraryScreen.SheetTitles().SequenceEqual(new[] { Bravo, Alpha }), 10, "Z-A order");
    }

    [Test]
    public void DeleteSheet_RemovesItFromLibraryAndDatabase()
    {
        LibraryScreen.OpenSheetMenu(Bravo);
        Ui.TapText(AppStrings.Delete);
        // Confirmation alert: its confirm button is also labelled "Delete".
        Ui.Poll(() => Ui.TextExists(AppStrings.DeleteSheetTitle), 10, "the delete confirmation");
        Ui.TapText(AppStrings.Delete);

        LibraryScreen.WaitSheetGone(Bravo);
        Assert.That(AppDb.Sheets().Select(s => s.Title), Is.EquivalentTo(new[] { Alpha }));
    }
}

/// <summary>Importing a PDF through the system document picker (empty library).</summary>
[TestFixture]
public class ImportTests : E2ETestBase
{
    protected override IReadOnlyList<SeedSheet> Sheets => Array.Empty<SeedSheet>();

    [Test]
    public void ImportPdf_ViaPicker_ShowsInLibraryAndOpensInViewer()
    {
        var pdf = PdfFactory.CreatePortrait(
            Path.Combine(Path.GetTempPath(), "piccolo-e2e", "Imported Score.pdf"), 3);

        // Empty library: the header button and the empty-state button both import.
        LibraryScreen.ImportPdfViaPicker(pdf);

        LibraryScreen.SheetRow("Imported Score");
        Assert.That(AppDb.Sheets().Select(s => s.Title), Does.Contain("Imported Score"));

        LibraryScreen.OpenSheet("Imported Score");
        ViewerScreen.RevealChrome();
        Assert.That(ViewerScreen.Indicator.Total, Is.EqualTo(3));
    }
}
