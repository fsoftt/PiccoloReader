using System.Globalization;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Converters;

// Secondary line of a sheet row: "12 pp. · 3/5/2026" (page count when known, then date added).
public class SheetMetaConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Sheet sheet)
        {
            return string.Empty;
        }

        var date = sheet.DateAdded.ToLocalTime().ToString("d", culture);
        return sheet.PageCount > 0
            ? $"{AppStrings.Pages(sheet.PageCount)} · {date}"
            : date;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// "{0} sheets" caption of a folder card.
public class SheetCountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AppStrings.SheetCount(value is int count ? count : 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
