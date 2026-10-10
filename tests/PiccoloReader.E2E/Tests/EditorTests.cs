using System.Drawing;
using OpenQA.Selenium;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>
/// Annotation editing. Strokes and symbols are painted on a SkiaSharp canvas, so there is nothing
/// in the accessibility tree to assert on: the app's SQLite database (read with adb run-as) is the
/// source of truth for what was drawn, moved, resized, erased or deleted.
/// </summary>
[TestFixture]
public class EditorTests : E2ETestBase
{
    [SetUp]
    public void OpenSample() => LibraryScreen.OpenSheet(SheetTitle);

    private static AnnotationRow OnlyRow(List<AnnotationRow> rows)
    {
        Assert.That(rows, Has.Count.EqualTo(1));
        return rows[0];
    }

    // ---- pencil ----

    [Test]
    public void Pencil_DrawsStroke_StoredInPageSpace()
    {
        ViewerScreen.DrawStroke();

        var stroke = OnlyRow(AppDb.WaitForAnnotations(r => r.Count == 1, "one stroke row after drawing"));

        Assert.That(stroke.IsStroke, Is.True);
        Assert.That(stroke.PageIndex, Is.EqualTo(0));
        Assert.That(stroke.CoordinateSpace, Is.EqualTo(1), "new annotations use page-normalized coordinates");
        var points = stroke.StrokePoints();
        Assert.That(points, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(points, Has.All.Matches<(double X, double Y)>(p => p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1),
            "every point lies inside the page");
        Assert.That(points.Max(p => p.X) - points.Min(p => p.X), Is.GreaterThan(0.2), "the stroke spans the swipe");
    }

    [Test]
    public void UndoRedo_TogglesStroke_AndEnabledStates()
    {
        ViewerScreen.DrawStroke();
        AppDb.WaitForAnnotations(r => r.Count == 1, "stroke stored");

        Assert.That(Ui.IsEnabled(ViewerScreen.UndoButton), Is.True, "undo is available after a stroke");
        Assert.That(Ui.IsEnabled(ViewerScreen.RedoButton), Is.False, "nothing to redo yet");

        Ui.Tap(ViewerScreen.UndoButton);
        AppDb.WaitForAnnotations(r => r.Count == 0, "undo removes the stroke");
        Ui.Poll(() => !Ui.IsEnabled(ViewerScreen.UndoButton), 10, "undo to disable when the history is empty");
        Assert.That(Ui.IsEnabled(ViewerScreen.RedoButton), Is.True, "redo is available after undo");

        Ui.Tap(ViewerScreen.RedoButton);
        AppDb.WaitForAnnotations(r => r.Count == 1, "redo restores the stroke");
        Ui.Poll(() => !Ui.IsEnabled(ViewerScreen.RedoButton), 10, "redo to disable again");
    }

    [Test]
    public void Eraser_RemovesTheStroke()
    {
        ViewerScreen.DrawStroke();
        AppDb.WaitForAnnotations(r => r.Count == 1, "stroke stored");

        ViewerScreen.ReopenToolSheet();
        ViewerScreen.SwitchSegment(Tool.Eraser);
        ViewerScreen.DragAlongStrokePath(200);

        AppDb.WaitForAnnotations(r => r.Count == 0, "the eraser deleted the stroke");
    }

    [Test]
    public void Done_EndsEditing_AndRestoresReadingBar()
    {
        ViewerScreen.DrawStroke();
        AppDb.WaitForAnnotations(r => r.Count == 1, "stroke stored");
        Assert.That(Ui.Exists(ViewerScreen.DoneButton), Is.True, "editing bar is shown while a tool is armed");
        Assert.That(Ui.Exists(ViewerScreen.ModeButton), Is.False, "reading controls are replaced while editing");

        ViewerScreen.EndEditing();

        Ui.Find(ViewerScreen.ModeButton);
        Assert.That(Ui.Exists(ViewerScreen.UndoButton), Is.False);
        Assert.That(AppDb.Annotations(), Has.Count.EqualTo(1), "finishing keeps what was drawn");
    }

    // ---- symbols ----

    [Test]
    public void Symbol_Placed_CreatesPageSpaceRowAtPageCenter()
    {
        ViewerScreen.PlaceFirstSymbol();

        var icon = OnlyRow(AppDb.WaitForAnnotations(r => r.Count == 1, "icon row after placing a symbol"));

        Assert.That(icon.IsIcon, Is.True);
        Assert.That(icon.IconKey, Is.EqualTo(ViewerScreen.FirstSymbolKey));
        Assert.That(icon.CoordinateSpace, Is.EqualTo(1));
        Assert.That(icon.X + icon.Width / 2, Is.EqualTo(0.5).Within(0.02), "centered horizontally");
        Assert.That(icon.Y + icon.Height / 2, Is.EqualTo(0.5).Within(0.02), "centered vertically");
        Assert.That(icon.Width, Is.GreaterThan(0));
    }

    [Test]
    public void SymbolCategoryTab_ShowsThatCategorysSymbols()
    {
        ViewerScreen.OpenToolSheet(Tool.Symbols);
        Ui.Find("SymbolItem_" + ViewerScreen.FirstSymbolKey);
        Assert.That(Ui.Exists("SymbolItem_articAccentAbove"), Is.False, "articulations are on another tab");

        Ui.Tap("SymbolCategoryTab_1");

        Ui.Find("SymbolItem_articAccentAbove");
        Assert.That(Ui.Exists("SymbolItem_" + ViewerScreen.FirstSymbolKey), Is.False);
    }

    [Test]
    public void Symbol_Moved_UpdatesPosition()
    {
        ViewerScreen.PlaceFirstSymbol();
        var before = OnlyRow(AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored"));

        var center = Ui.CenterOf(ViewerScreen.SelectionBorder);
        Gestures.Drag(center, new Point(center.X + 150, center.Y - 250), 700);

        var after = OnlyRow(AppDb.WaitForAnnotations(
            r => r.Count == 1 && Math.Abs(r[0].X - before.X) > 0.05, "the moved position to be saved"));
        Assert.That(after.X, Is.GreaterThan(before.X), "moved right");
        Assert.That(after.Y, Is.LessThan(before.Y), "moved up");
        Assert.That(after.Width, Is.EqualTo(before.Width).Within(1e-6), "moving does not resize");
    }

    [Test]
    public void Symbol_Resized_UpdatesSize()
    {
        ViewerScreen.PlaceFirstSymbol();
        var before = OnlyRow(AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored"));

        var handle = Ui.CenterOf(ViewerScreen.ResizeHandle);
        Gestures.Drag(handle, new Point(handle.X + 160, handle.Y + 100), 700);

        var after = OnlyRow(AppDb.WaitForAnnotations(
            r => r.Count == 1 && r[0].Width > before.Width + 0.02, "the resized width to be saved"));
        Assert.That(after.Width, Is.GreaterThan(before.Width));
        Assert.That(after.X, Is.EqualTo(before.X).Within(1e-6), "resizing keeps the top-left corner");
    }

    [Test]
    public void Symbol_DeletedWithTrashTarget_RemovesRow()
    {
        ViewerScreen.PlaceFirstSymbol();
        AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored");

        Ui.Tap(ViewerScreen.TrashTarget);

        AppDb.WaitForAnnotations(r => r.Count == 0, "the trash button deleted the icon");
        Ui.WaitGone(ViewerScreen.SelectionBorder);
    }

    [Test]
    public void UndoRedo_TogglesPlacedSymbol()
    {
        ViewerScreen.PlaceFirstSymbol();
        AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored");

        Ui.Tap(ViewerScreen.UndoButton);
        AppDb.WaitForAnnotations(r => r.Count == 0, "undo removes the icon");

        // Undoing the placement ends the selection, so the editing bar is gone: redo is in the "more" menu.
        Ui.Tap("ViewerMoreButton");
        Ui.TapText(AppStrings.RedoAction);
        var rows = AppDb.WaitForAnnotations(r => r.Count == 1, "redo restores the icon");
        Assert.That(rows[0].IconKey, Is.EqualTo(ViewerScreen.FirstSymbolKey));
    }

    // ---- rotation / zoom ----

    [Test]
    public void Rotation_WhileEditing_KeepsAnnotationRowsIdentical()
    {
        ViewerScreen.PlaceFirstSymbol();
        var before = AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored");

        try
        {
            AppSession.Driver.Orientation = ScreenOrientation.Landscape;
            Thread.Sleep(2500);
            Assert.That(Ui.Exists(ViewerScreen.DoneButton), Is.True, "still editing after rotating");

            AppSession.Driver.Orientation = ScreenOrientation.Portrait;
            Thread.Sleep(2500);
        }
        finally
        {
            AppSession.Driver.Orientation = ScreenOrientation.Portrait;
        }

        Assert.That(Ui.Exists(ViewerScreen.DoneButton), Is.True, "still editing after rotating back");
        Assert.That(AppDb.Annotations(), Is.EqualTo(before), "rotation must not rewrite or move annotations");
    }

    [Test]
    public void Pinch_ZoomsThePageInTheEditor()
    {
        ViewerScreen.PlaceFirstSymbol();
        var before = Ui.RectOf(ViewerScreen.SelectionBorder);

        var area = ViewerScreen.PageArea;
        Gestures.Pinch(new Point(area.X + area.Width / 2, area.Y + area.Height / 2), 80, 300);

        Ui.Poll(() => Ui.RectOf(ViewerScreen.SelectionBorder).Width > before.Width * 1.3, 10, "the selection to scale up with the page");
    }

    // ---- continuous mode ----

    protected override string InitialReadingMode => "Horizontal";

    [Test]
    public void ContinuousMode_TappingAnIconInTheList_OpensTheEditor()
    {
        ViewerScreen.PlaceFirstSymbol();
        var icon = OnlyRow(AppDb.WaitForAnnotations(r => r.Count == 1, "icon stored"));
        ViewerScreen.EndEditing();

        ViewerScreen.RevealChrome();
        ViewerScreen.CycleReadingMode();
        ViewerScreen.CycleReadingMode();
        var host = Ui.Find(ViewerScreen.ContinuousHost, 10);
        Assert.That(Ui.Exists(ViewerScreen.DoneButton), Is.False, "the list itself is not an editing state");
        Thread.Sleep(2000); // let the first page render and annotations load

        // Page 1 is at the top of the list, as wide as the list; its height follows the PDF's aspect ratio.
        var size = host.Size;
        var pageHeight = Math.Round(size.Width * PdfFactory.PortraitHeight / PdfFactory.PortraitWidth);
        var tap = new Point(
            host.Location.X + (int)(size.Width * (icon.X + icon.Width / 2)),
            host.Location.Y + (int)(pageHeight * (icon.Y + icon.Height / 2)));
        Gestures.Tap(tap);

        Ui.Find(ViewerScreen.DoneButton, 10);
        Ui.Find(ViewerScreen.SelectionBorder, 10);
        Assert.That(Ui.Exists(ViewerScreen.PageContainer), Is.True, "the page opens in the single-page editor");

        ViewerScreen.EndEditing();
        Ui.Find(ViewerScreen.ContinuousHost, 10);
    }
}
