using System.Drawing;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>
/// Figure tools: copy from the long-press menu, slurs, shape recognition of pencil strokes and text
/// annotations. Like the other editor tests they assert on the app database.
/// </summary>
[TestFixture]
public class FigureTests : E2ETestBase
{
    private const string Crescendo = "dynamicCrescendoHairpin";

    // The phrasing category (slurs) is the last one in the catalog (index 11).
    private const string PhrasingTab = "SymbolCategoryTab_11";

    [SetUp]
    public void OpenSample() => LibraryScreen.OpenSheet(SheetTitle);

    private static void PlaceSymbol(string key, string? categoryTab = null)
    {
        ViewerScreen.OpenToolSheet(Tool.Symbols);
        if (categoryTab is not null)
        {
            Ui.Tap(categoryTab);
        }

        Ui.Tap("SymbolItem_" + key);
        Ui.Find(ViewerScreen.SelectionBorder);
    }

    private static Point PagePoint(double fx, double fy)
    {
        var area = ViewerScreen.PageArea;
        return new Point(area.X + (int)(area.Width * fx), area.Y + (int)(area.Height * fy));
    }

    private static void DrawPencil(IEnumerable<(double X, double Y)> path, int msPerSegment = 120)
    {
        ViewerScreen.OpenToolSheet(Tool.Pencil);
        Gestures.Polyline(path.Select(p => PagePoint(p.X, p.Y)).ToList(), msPerSegment);
    }

    private static IEnumerable<(double X, double Y)> Arc(double bulge) =>
        Enumerable.Range(0, 11).Select(i =>
        {
            var t = i / 10.0;
            return (0.25 + 0.5 * t, 0.5 - bulge * Math.Sin(Math.PI * t));
        });

    private static readonly (double X, double Y)[] LessThan = { (0.75, 0.45), (0.5, 0.475), (0.25, 0.5), (0.5, 0.525), (0.75, 0.55) };

    private static readonly (double X, double Y)[] ZigZag =
    {
        (0.25, 0.45), (0.33, 0.55), (0.41, 0.45), (0.49, 0.55), (0.57, 0.45), (0.65, 0.55), (0.73, 0.45)
    };

    // ---- copy ----

    [Test, Category("Gesture")]
    public void LongPressOnFigure_Copy_DuplicatesItOffsetAndSelected()
    {
        PlaceSymbol(Crescendo);
        var original = AppDb.WaitForAnnotations(r => r.Count == 1, "crescendo stored").Single();

        Gestures.LongPress(Ui.CenterOf(ViewerScreen.SelectionBorder), 800);
        Ui.TapText(AppStrings.CopyFigure);

        var rows = AppDb.WaitForAnnotations(r => r.Count == 2, "the copy to be stored");
        Assert.That(rows.Select(r => r.IconKey), Is.All.EqualTo(Crescendo));
        var copy = rows.Single(r => r.Id != original.Id);
        Assert.That(copy.Width, Is.EqualTo(original.Width).Within(1e-6));
        Assert.That(copy.Height, Is.EqualTo(original.Height).Within(1e-6));
        Assert.That(Math.Abs(copy.X - original.X) + Math.Abs(copy.Y - original.Y), Is.GreaterThan(0.005), "the copy is offset");
        Assert.That(Ui.Exists(ViewerScreen.SelectionBorder), Is.True, "the new figure is selected");
    }

    [Test, Category("Gesture")]
    public void LongPress_InPlainReading_ShowsNoMenu()
    {
        PlaceSymbol(Crescendo);
        AppDb.WaitForAnnotations(r => r.Count == 1, "crescendo stored");
        ViewerScreen.EndEditing();

        Gestures.LongPress(PagePoint(0.5, 0.5), 800);
        Thread.Sleep(1500);

        Assert.That(Ui.TextExists(AppStrings.CopyFigure), Is.False, "no figure menu while reading");
        Assert.That(AppDb.Annotations(), Has.Count.EqualTo(1));
    }

    // ---- slur ----

    [Test]
    public void Slur_PlacedMovedAndDeleted()
    {
        PlaceSymbol("slurAbove", PhrasingTab);
        var before = AppDb.WaitForAnnotations(r => r.Count == 1, "slur stored").Single();
        Assert.That(before.IconKey, Is.EqualTo("slurAbove"));
        Assert.That(before.IsIcon, Is.True);

        var center = Ui.CenterOf(ViewerScreen.SelectionBorder);
        Gestures.Drag(center, new Point(center.X + 150, center.Y - 200), 700);
        var after = AppDb.WaitForAnnotations(r => r.Count == 1 && Math.Abs(r[0].X - before.X) > 0.05, "the moved slur to be saved").Single();
        Assert.That(after.X, Is.GreaterThan(before.X));
        Assert.That(after.Y, Is.LessThan(before.Y));

        Ui.Tap(ViewerScreen.TrashTarget);
        AppDb.WaitForAnnotations(r => r.Count == 0, "the trash button deleted the slur");
    }

    // ---- recognition ----

    [Test, Category("Gesture")]
    public void PencilArc_BecomesASlurIcon_WithoutAStrokeRow()
    {
        DrawPencil(Arc(0.08));

        var rows = AppDb.WaitForAnnotations(r => r.Count >= 1 && r.All(a => a.IsIcon), "the arc to be converted to an icon");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].IconKey, Is.EqualTo("slurAbove").Or.EqualTo("slurBelow"));
        Assert.That(rows.Any(r => r.IsStroke), Is.False);
    }

    [Test, Category("Gesture")]
    public void PencilLessThan_BecomesACrescendo_AndUndoBringsBackTheStroke()
    {
        DrawPencil(LessThan);

        var rows = AppDb.WaitForAnnotations(r => r.Count == 1 && r[0].IsIcon, "the '<' to be converted to a hairpin");
        Assert.That(rows[0].IconKey, Is.EqualTo(Crescendo));

        Ui.Tap(ViewerScreen.UndoButton);
        rows = AppDb.WaitForAnnotations(r => r.Count == 1 && r[0].IsStroke, "undo to restore the stroke and remove the icon");
        Assert.That(rows.Any(r => r.IsIcon), Is.False);
    }

    [Test, Category("Gesture")]
    public void PencilZigZag_StaysAStroke()
    {
        DrawPencil(ZigZag, 150);

        var rows = AppDb.WaitForAnnotations(r => r.Count >= 1, "the scribble to be stored");
        Thread.Sleep(1500);
        rows = AppDb.Annotations();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].IsStroke, Is.True, "a scribble is not a figure");
    }

    // ---- text ----

    private static void PlaceText(string text)
    {
        ViewerScreen.OpenToolSheet(Tool.Pencil);
        ViewerScreen.SwitchSegment(Tool.Text);

        // The first tap may only close the tool sheet.
        for (var attempt = 0; attempt < 5 && !HasPrompt(); attempt++)
        {
            Gestures.Tap(PagePoint(0.4, 0.35));
            Thread.Sleep(1500);
        }

        var field = Ui.FindDialogEditText(15);
        field.SendKeys(text);
        Ui.TapText(AppStrings.OK);
    }

    private static bool HasPrompt() => Ui.TextExists(AppStrings.AddTextTitle);

    [Test, Category("Gesture")]
    public void TextTool_AddsText_EditsItAndUndoRestoresTheOldText()
    {
        PlaceText("Allegro");
        var row = AppDb.WaitForAnnotations(r => r.Count == 1 && r[0].Text == "Allegro", "the text annotation to be stored").Single();
        Assert.That(row.Type, Is.EqualTo("Text"));

        Ui.Find(ViewerScreen.SelectionBorder);
        Gestures.LongPress(Ui.CenterOf(ViewerScreen.SelectionBorder), 800);
        Ui.TapText(AppStrings.EditText);
        var field = Ui.FindDialogEditText(15);
        field.Clear();
        field.SendKeys("Andante");
        Ui.TapText(AppStrings.OK);
        AppDb.WaitForAnnotations(r => r.Count == 1 && r[0].Text == "Andante", "the edited text to be saved");

        Ui.Tap(ViewerScreen.UndoButton);
        AppDb.WaitForAnnotations(r => r.Count == 1 && r[0].Text == "Allegro", "undo to restore the previous text");
    }
}

/// <summary>The "recognize drawn slurs and hairpins" setting turns shape recognition off.</summary>
[TestFixture]
public class RecognitionSettingTests : E2ETestBase
{
    [Test, Category("Gesture")]
    public void RecognitionOff_ArcStaysAStroke()
    {
        if (Ui.TryFind("TabSettings") is { } tab)
        {
            tab.Click();
        }
        else
        {
            Ui.TapText(AppStrings.Settings);
        }

        Ui.Find("SettingsLanguageRow", 15);
        Ui.ScrollIntoView("SettingsRecognizeShapesSwitch");
        Assert.That(Ui.IsChecked("SettingsRecognizeShapesSwitch"), Is.True, "recognition is on by default");
        Ui.Tap("SettingsRecognizeShapesSwitch");
        Ui.Poll(() => !Ui.IsChecked("SettingsRecognizeShapesSwitch"), 5, "the switch to turn off");

        if (Ui.TryFind("TabLibrary") is { } library)
        {
            library.Click();
        }
        else
        {
            Ui.TapText(AppStrings.Library);
        }

        LibraryScreen.OpenSheet(SheetTitle);
        ViewerScreen.OpenToolSheet(Tool.Pencil);
        var area = ViewerScreen.PageArea;
        var points = Enumerable.Range(0, 11).Select(i =>
        {
            var t = i / 10.0;
            return new Point(area.X + (int)(area.Width * (0.25 + 0.5 * t)), area.Y + (int)(area.Height * (0.5 - 0.08 * Math.Sin(Math.PI * t))));
        }).ToList();
        Gestures.Polyline(points, 120);

        AppDb.WaitForAnnotations(r => r.Count >= 1, "the arc to be stored");
        Thread.Sleep(1500);
        var rows = AppDb.Annotations();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].IsStroke, Is.True, "with recognition off the arc stays a stroke");
    }
}
