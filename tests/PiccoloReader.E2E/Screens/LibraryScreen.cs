using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E.Screens;

/// <summary>Library and folder pages (they share row ids).</summary>
public static class LibraryScreen
{
    public const string ImportButton = "LibraryImportButton";
    public const string NewFolderButton = "LibraryNewFolderButton";
    public const string SortButton = "LibrarySortButton";
    public const string SearchEntry = "LibrarySearchEntry";
    public const string SheetTitleId = "LibrarySheetTitle";
    public const string FolderNameId = "LibraryFolderName";
    public const string MoreButton = "LibraryItemMoreButton";

    public static void WaitLoaded() => Ui.Find(ImportButton, 60);

    // ---- sheets ----

    /// <summary>Titles of the visible sheet rows, top to bottom.</summary>
    public static IReadOnlyList<string> SheetTitles() =>
        Ui.FindAll(SheetTitleId).OrderBy(e => e.Location.Y).Select(e => e.Text).ToList();

    public static IWebElement SheetRow(string title)
    {
        IWebElement? row = null;
        Ui.Poll(
            () => (row = Ui.FindAll(SheetTitleId).FirstOrDefault(e => e.Text == title)) is not null,
            20, $"sheet '{title}' in the list");
        return row!;
    }

    public static bool HasSheet(string title) => Ui.FindAll(SheetTitleId).Any(e => e.Text == title);

    public static void WaitSheetGone(string title) =>
        Ui.Poll(() => !HasSheet(title), 15, $"sheet '{title}' to leave the list");

    public static void OpenSheet(string title)
    {
        SheetRow(title).Click();
        ViewerScreen.WaitOpened();
    }

    /// <summary>The "more" button on the same row as the given sheet title.</summary>
    public static IWebElement MoreButtonOf(string title)
    {
        var rowY = SheetRow(title).Location.Y;
        return Ui.FindAll(MoreButton).OrderBy(b => Math.Abs(b.Location.Y + b.Size.Height / 2 - rowY)).First();
    }

    public static void OpenSheetMenu(string title)
    {
        MoreButtonOf(title).Click();
        Ui.FindText(AppStrings.Move);
    }

    public static void LongPressSheet(string title) => Gestures.LongPress(SheetRow(title));

    public static bool SheetMenuIsShown() => Ui.TextExists(AppStrings.Move) && Ui.TextExists(AppStrings.Delete);

    public static void DismissMenu()
    {
        Ui.TapText(AppStrings.Cancel);
        Ui.Poll(() => !Ui.TextExists(AppStrings.Cancel), 10, "the menu to close");
    }

    // ---- folders ----

    public static IReadOnlyList<string> FolderNames() =>
        Ui.FindAll(FolderNameId).Select(e => e.Text).ToList();

    public static void CreateFolder(string name)
    {
        Ui.Tap(NewFolderButton);
        var field = Ui.FindDialogEditText();
        field.SendKeys(name);
        Ui.TapText(AppStrings.OK);
        Ui.Poll(() => FolderNames().Contains(name), 30, $"folder '{name}' to appear");
    }

    public static void OpenFolder(string name)
    {
        Ui.Poll(() => FolderNames().Contains(name), 30, $"folder '{name}'");
        Ui.FindAll(FolderNameId).First(e => e.Text == name).Click();
        Ui.Find("FolderBackButton");
    }

    public static void BackFromFolder()
    {
        Ui.Tap("FolderBackButton");
        Ui.Find(ImportButton);
    }

    // ---- import through the system document picker ----

    /// <summary>Pushes the PDF to Downloads and imports it by tapping the import button + the system picker.</summary>
    public static void ImportPdfViaPicker(string localPdfPath, string importButtonId = ImportButton)
    {
        var fileName = Path.GetFileName(localPdfPath);
        var devicePath = $"/sdcard/Download/{fileName}";
        Adb.Run("push", localPdfPath, devicePath);
        // Make the file visible to the picker (MediaStore-backed Downloads root).
        Adb.Run(true, "shell", $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d file://{devicePath}");

        Ui.Tap(importButtonId);
        PickInDocumentsUi(fileName);
    }

    private static bool PickerIsOpen() =>
        AppSession.Driver.FindElements(
            MobileBy.AndroidUIAutomator("new UiSelector().packageNameMatches(\".*documentsui.*\")")).Count > 0;

    private static void PickInDocumentsUi(string fileName)
    {
        Ui.Poll(PickerIsOpen, 30, "the system document picker");
        Thread.Sleep(1500);

        if (!Ui.TextExists(fileName))
        {
            // Not in the picker's starting folder: open the roots drawer and go to Downloads.
            var roots = AppSession.Driver.FindElements(
                MobileBy.AndroidUIAutomator("new UiSelector().descriptionContains(\"Show roots\")")).FirstOrDefault();
            roots?.Click();
            Ui.TapText("Downloads", 15);
            Thread.Sleep(1000);
        }

        Ui.FindText(fileName, 20).Click();

        // In multi-select pickers a tap may only select the file; confirm with the action button.
        Ui.Poll(() =>
        {
            if (!PickerIsOpen())
            {
                return true;
            }

            foreach (var label in new[] { "Open", "Select", "OPEN", "SELECT" })
            {
                var button = Ui.TryFindText(label);
                if (button is not null)
                {
                    button.Click();
                    break;
                }
            }

            return false;
        }, 20, "the picker to close after choosing the file");
    }
}
