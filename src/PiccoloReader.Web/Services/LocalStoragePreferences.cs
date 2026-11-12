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
