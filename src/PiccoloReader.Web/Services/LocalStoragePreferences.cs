using System.Globalization;
using PiccoloReader.Core.Services.Stylus;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Web.Services;

// Core preference interfaces over localStorage (counterparts of the MAUI Preferences classes).
public sealed class LocalStorageReadingPreferenceService(BrowserInterop browser) : IReadingPreferenceService
{
    private const string Key = "piccolo.readingMode";

    public ReadingMode GetReadingMode() =>
        Enum.TryParse<ReadingMode>(browser.GetLocalStorageSync(Key), out var m) && Enum.IsDefined(m) ? m : ReadingMode.Horizontal;

    public void SetReadingMode(ReadingMode mode) => browser.SetLocalStorageSync(Key, mode.ToString());
}

public sealed class LocalStorageSortPreferenceService(BrowserInterop browser) : ISortPreferenceService
{
    private const string FieldKey = "piccolo.sheetSortField";
    private const string DirectionKey = "piccolo.sheetSortDirection";

    public SortField SheetSortField =>
        Enum.TryParse<SortField>(browser.GetLocalStorageSync(FieldKey), out var f) && Enum.IsDefined(f) ? f : SortField.Name;

    public SortDirection SheetSortDirection =>
        Enum.TryParse<SortDirection>(browser.GetLocalStorageSync(DirectionKey), out var d) && Enum.IsDefined(d) ? d : SortDirection.Ascending;

    public void Save(SortField field, SortDirection direction)
    {
        browser.SetLocalStorageSync(FieldKey, field.ToString());
        browser.SetLocalStorageSync(DirectionKey, direction.ToString());
    }
}

public sealed class LocalStorageConcertModePreferenceService(BrowserInterop browser) : IConcertModePreferenceService
{
    private const string Key = "piccolo.concertMode";

    public bool Enabled
    {
        get => browser.GetLocalStorageSync(Key) != "0";
        set => browser.SetLocalStorageSync(Key, value ? "1" : "0");
    }
}

// Last-used annotation tool options (same fields as the Android preferences).
public sealed class LocalStorageAnnotationToolPreferenceService(BrowserInterop browser) : IAnnotationToolPreferenceService
{
    private AnnotationToolSettings? _cached;

    public AnnotationToolSettings Load() => _cached ??= Read();

    public void Save(AnnotationToolSettings settings)
    {
        var s = _cached = settings.Sanitized();
        Set("pencilColor", s.PencilColorHex);
        Set("pencilWidth", s.PencilStrokeWidth.ToString(CultureInfo.InvariantCulture));
        Set("highlighterColor", s.HighlighterColorHex);
        Set("highlighterWidth", s.HighlighterStrokeWidth.ToString(CultureInfo.InvariantCulture));
        Set("eraserRadius", s.EraserRadius.ToString(CultureInfo.InvariantCulture));
        Set("textColor", s.TextColorHex);
    }

    private void Set(string key, string value) => browser.SetLocalStorageSync("piccolo.tool." + key, value);

    private double Num(string key, double fallback) =>
        double.TryParse(browser.GetLocalStorageSync("piccolo.tool." + key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private AnnotationToolSettings Read()
    {
        var d = new AnnotationToolSettings();
        return new AnnotationToolSettings
        {
            PencilColorHex = browser.GetLocalStorageSync("piccolo.tool.pencilColor") ?? d.PencilColorHex,
            PencilStrokeWidth = Num("pencilWidth", d.PencilStrokeWidth),
            HighlighterColorHex = browser.GetLocalStorageSync("piccolo.tool.highlighterColor") ?? d.HighlighterColorHex,
            HighlighterStrokeWidth = Num("highlighterWidth", d.HighlighterStrokeWidth),
            EraserRadius = Num("eraserRadius", d.EraserRadius),
            TextColorHex = browser.GetLocalStorageSync("piccolo.tool.textColor") ?? d.TextColorHex,
        }.Sanitized();
    }
}

public sealed class LocalStorageStylusPreferenceService(BrowserInterop browser) : IStylusPreferenceService
{
    public bool DrawsDirectly
    {
        get => browser.GetLocalStorageSync("piccolo.stylusDirect") != "0";
        set => browser.SetLocalStorageSync("piccolo.stylusDirect", value ? "1" : "0");
    }

    public bool RecognizesShapes
    {
        get => browser.GetLocalStorageSync("piccolo.recognizeShapes") != "0";
        set => browser.SetLocalStorageSync("piccolo.recognizeShapes", value ? "1" : "0");
    }

    public int ShapeHintsShown
    {
        get => int.TryParse(browser.GetLocalStorageSync("piccolo.shapeHints"), out var n) ? n : 0;
        set => browser.SetLocalStorageSync("piccolo.shapeHints", value.ToString(CultureInfo.InvariantCulture));
    }
}
