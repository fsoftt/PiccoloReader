using System.Globalization;
using System.Text;

namespace PiccoloReader.Core.Services;

public static class FolderNameNormalizer
{
    // string.IsNullOrWhiteSpace alone isn't enough: invisible characters
    // that aren't classed as whitespace (zero-width space U+200B, BOM
    // U+FEFF, Hangul filler U+3164, control chars...) slip past it and
    // produce a folder whose name renders as blank. Strip those, collapse
    // the rest, and treat an empty result as "no name".
    public static string? Normalize(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                || c is 'ㅤ' or 'ᅟ' or 'ᅠ' or 'ﾠ' or '⠀')
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
