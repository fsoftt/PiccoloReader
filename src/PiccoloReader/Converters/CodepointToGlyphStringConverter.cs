using System.Globalization;

namespace PiccoloReader.Converters;

public class CodepointToGlyphStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int codepoint ? char.ConvertFromUtf32(codepoint) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
