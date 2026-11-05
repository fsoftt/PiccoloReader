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
        LibraryScreen.ConfirmDelete();

        LibraryScreen.WaitSheetGone(Bravo);
        Assert.That(AppDb.Sheets().Select(s => s.Title), Is.EquivalentTo(new[] { Alpha }));
    }

    // The library mirrors every sheet into Documents/PiccoloReader (the visible copy). Deleting
    // removes the sheet from the library and the private copy; the visible copy goes only when
    // the "also delete the file from storage" checkbox is ticked (PR 175).
    private static string ExternalCopy(string title) => $"/sdcard/Documents/PiccoloReader/{title}.pdf";

    // The checkbox only shows for sheets already mirrored when the library list was loaded.
    private static void MirrorThenRelaunch()
    {
        Ui.Poll(() => Adb.SharedFileExists(ExternalCopy(Bravo)) && Adb.SharedFileExists(ExternalCopy(Alpha)), 60, "the sheets to be mirrored into Documents/PiccoloReader");
        Thread.Sleep(2000); // the sheet row gets its external path right after the file is written
        AppSession.Relaunch();
    }

    [TearDown]
    public void RemoveExternalCopies()
    {
        Adb.DeleteSharedFile(ExternalCopy(Alpha));
        Adb.DeleteSharedFile(ExternalCopy(Bravo));
    }

    [Test]
    public void DeleteSheet_WithFileCheckbox_RemovesTheStorageCopyToo()
    {
        MirrorThenRelaunch();
        var privateCopy = $"files/Sheets/{AppDb.Sheets().Single(s => s.Title == Bravo).FileName}";

        LibraryScreen.OpenSheetMenu(Bravo);
        Ui.TapText(AppStrings.Delete);
        Ui.Find(LibraryScreen.DeleteConfirmOverlay, 10);
        Assert.That(Ui.IsChecked(LibraryScreen.DeleteConfirmCheckBox), Is.False, "the delete-file checkbox starts unchecked");
        LibraryScreen.ConfirmDelete(alsoDeleteFile: true);

        LibraryScreen.WaitSheetGone(Bravo);
        Ui.Poll(() => !Adb.SharedFileExists(ExternalCopy(Bravo)), 30, "the storage copy to be deleted");
        Assert.That(Adb.RunAs($"ls {privateCopy}", allowFailure: true), Does.Not.Contain(".pdf"), "the private copy is removed too");
        Assert.That(Adb.SharedFileExists(ExternalCopy(Alpha)), Is.True, "the other sheet's storage copy is untouched");
    }

    [Test]
    public void DeleteSheet_WithoutFileCheckbox_KeepsTheStorageCopy()
    {
        MirrorThenRelaunch();
        var privateCopy = $"files/Sheets/{AppDb.Sheets().Single(s => s.Title == Bravo).FileName}";

        LibraryScreen.OpenSheetMenu(Bravo);
        Ui.TapText(AppStrings.Delete);
        Ui.Find(LibraryScreen.DeleteConfirmOverlay, 10);
        Assert.That(Ui.IsChecked(LibraryScreen.DeleteConfirmCheckBox), Is.False, "the delete-file checkbox starts unchecked");
        LibraryScreen.ConfirmDelete();

        LibraryScreen.WaitSheetGone(Bravo);
        Assert.That(AppDb.Sheets().Select(s => s.Title), Is.EquivalentTo(new[] { Alpha }));
        Assert.That(Adb.RunAs($"ls {privateCopy}", allowFailure: true), Does.Not.Contain(".pdf"), "the private copy is removed");
        Thread.Sleep(2500); // give a (wrong) asynchronous storage delete time to happen
        Assert.That(Adb.SharedFileExists(ExternalCopy(Bravo)), Is.True, "the storage copy stays when the checkbox is not ticked");
    }
}
