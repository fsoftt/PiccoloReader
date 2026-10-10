using System.Drawing;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E.Screens;

public enum Tool { Symbols, Pencil, Eraser }

/// <summary>Page object for the sheet viewer / editor (SheetViewerPage), keyed on AutomationIds.</summary>
public static class ViewerScreen
{
    public const string PageContainer = "ViewerPageContainer";
    public const string ContinuousHost = "ViewerContinuousHost";
    public const string AnnotationCanvas = "AnnotationCanvas";
    public const string PageIndicator = "ViewerPageIndicator";
    public const string ToolsFab = "ViewerToolsFab";
    public const string ModeButton = "ViewerModeButton";
    public const string BookmarkButton = "ViewerBookmarkButton";
    public const string BackButton = "ViewerBackButton";
    public const string UndoButton = "EditorUndoButton";
    public const string RedoButton = "EditorRedoButton";
    public const string DoneButton = "EditorDoneButton";
    public const string ToolSheet = "ToolSheet";
    public const string SelectionBorder = "SelectionBorder";
    public const string ResizeHandle = "ResizeHandle";
    public const string TrashTarget = "TrashTarget";

    /// <summary>First symbol of the first category (a dynamic "f").</summary>
    public const string FirstSymbolKey = "dynamicForte";

    /// <summary>Waits until the viewer is showing either the paged page or the continuous list.</summary>
    public static void WaitOpened() =>
        Ui.Poll(() => Ui.Exists(PageContainer) || Ui.Exists(ContinuousHost), 45, "the viewer to open");

    public static bool IsOpen => Ui.Exists(PageContainer) || Ui.Exists(ContinuousHost);

    public static bool IsContinuous => Ui.Exists(ContinuousHost);

    // ---- chrome ----

    /// <summary>The page pill is only shown together with the rest of the reading chrome.</summary>
    public static bool ChromeVisible => Ui.Exists(PageIndicator);

    /// <summary>The viewer opens with its chrome hidden; a tap in the middle of the page toggles it.</summary>
    public static void RevealChrome()
    {
        if (ChromeVisible)
        {
            return;
        }

        TapPageCenter();
        Ui.Find(PageIndicator, 10);
    }

    public static void TapPageCenter() => Gestures.TapIn(Ui.Find(IsContinuous ? ContinuousHost : PageContainer), 0.5, 0.5);

    /// <summary>Screen rectangle of the paged reading surface (also where the drawing views sit).</summary>
    public static Rectangle PageArea => Ui.RectOf(PageContainer);

    // ---- paging ----

    public static string IndicatorText => Ui.TextOf(PageIndicator);

    /// <summary>(current, total) parsed from the localized "Page x of y" pill.</summary>
    public static (int Current, int Total) Indicator
    {
        get
        {
            var numbers = Regex.Matches(IndicatorText, @"\d+").Select(m => int.Parse(m.Value)).ToList();
            Assert.That(numbers, Has.Count.GreaterThanOrEqualTo(2), $"Unparseable page indicator '{IndicatorText}'");
            return (numbers[0], numbers[1]);
        }
    }

    public static void WaitForPage(int page) =>
        Ui.Poll(() => ChromeVisible && Indicator.Current == page, 15, $"page indicator to show page {page}");

    /// <summary>The outer 30% of the page area turn pages (right side in horizontal reading).</summary>
    public static void TapNextPageZone() => Gestures.TapIn(Ui.Find(PageContainer), 0.92, 0.5);

    public static void TapPreviousPageZone() => Gestures.TapIn(Ui.Find(PageContainer), 0.08, 0.5);

    /// <summary>In vertical paged reading the zones sit at the bottom/top edge.</summary>
    public static void TapBottomZone() => Gestures.TapIn(Ui.Find(PageContainer), 0.12, 0.95); // clear of the page pill and the tools FAB

    public static void TapTopZone() => Gestures.TapIn(Ui.Find(PageContainer), 0.5, 0.08);

    public static void GoToPage(int page)
    {
        Ui.Tap(PageIndicator);
        var field = Ui.FindDialogEditText();
        field.Clear();
        field.SendKeys(page.ToString());
        Ui.TapText(AppStrings.OK);
        Ui.Poll(() => !Ui.TextExists(AppStrings.GoToPageTitle), 10, "the go-to-page dialog to close");
    }

    public static void CycleReadingMode() => Ui.Tap(ModeButton);

    // ---- bookmarks ----

    /// <summary>Adds a bookmark for the current page through the bookmark menu + overlay.</summary>
    public static void AddBookmarkForCurrentPage()
    {
        Ui.Tap(BookmarkButton);
        Ui.TapText(AppStrings.AddBookmarkOption);
        Ui.Tap("AddBookmarkConfirmButton");
        Ui.Poll(() => !Ui.Exists("AddBookmarkConfirmButton"), 10, "the bookmark overlay to close");
    }

    // ---- tools ----

    public static void OpenToolSheet(Tool tool)
    {
        RevealChrome();
        Ui.Tap(ToolsFab);
        var id = tool switch
        {
            Tool.Symbols => "ViewerToolIcons",
            Tool.Pencil => "ViewerToolPencil",
            _ => "ViewerToolEraser"
        };
        Ui.Tap(id);
        Ui.Find(ToolSheet);
    }

    /// <summary>Switch tool from inside an open tool sheet.</summary>
    public static void SwitchSegment(Tool tool)
    {
        var id = tool switch
        {
            Tool.Symbols => "ToolSegmentSymbols",
            Tool.Pencil => "ToolSegmentPencil",
            _ => "ToolSegmentEraser"
        };
        Ui.Tap(id);
    }

    /// <summary>Reopens the tool sheet while a drawing tool is armed (the main FAB does that).</summary>
    public static void ReopenToolSheet()
    {
        Ui.Tap(ToolsFab);
        Ui.Find(ToolSheet);
    }

    /// <summary>A short gentle curve through the middle of the page, as fractions of the drawing surface.</summary>
    public static readonly (double X, double Y)[] StrokePath =
    {
        (0.25, 0.45), (0.38, 0.52), (0.50, 0.46), (0.62, 0.54), (0.75, 0.48)
    };

    public static void DragAlongStrokePath(int msPerSegment = 250)
    {
        var area = PageArea;
        var points = StrokePath
            .Select(p => new Point(area.X + (int)(area.Width * p.X), area.Y + (int)(area.Height * p.Y)))
            .ToList();
        Gestures.Polyline(points, msPerSegment);
    }

    /// <summary>Opens the pencil, draws one stroke across the page and waits for it to be stored.</summary>
    public static void DrawStroke()
    {
        OpenToolSheet(Tool.Pencil);
        DragAlongStrokePath();
    }

    public static void ToggleChromeByTap() => TapPageCenter();

    /// <summary>Places the first symbol; it ends up selected in the middle of the page.</summary>
    public static void PlaceFirstSymbol()
    {
        OpenToolSheet(Tool.Symbols);
        Ui.Tap("SymbolItem_" + FirstSymbolKey);
        Ui.Find(SelectionBorder);
    }

    public static void EndEditing()
    {
        Ui.Tap(DoneButton);
        Ui.Poll(() => !Ui.Exists(DoneButton), 10, "the editing bar to close");
    }
}
