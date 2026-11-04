namespace PiccoloReader.Core.ViewModels;

// Highlighter colors. Strokes are stored like pencil strokes, only with an
// ARGB "#AARRGGBB" color so the page shows through; no schema change.
public static class HighlighterPalette
{
    public const string AlphaHex = "66";

    public const double DefaultStrokeWidth = 0.025;

    public const double MinStrokeWidth = 0.01;

    public const double MaxStrokeWidth = 0.05;

    // Opaque swatch colors, the first one is the default.
    public static IReadOnlyList<string> SwatchColors { get; } =
        new[] { "#FFEB3B", "#76FF03", "#FF4081", "#40C4FF", "#FFAB40" };

    // The stored (translucent) colors, in the same order as SwatchColors.
    public static IReadOnlyList<string> Colors { get; } = SwatchColors.Select(ToStored).ToArray();

    public static string Default => Colors[0];

    // "#RRGGBB" -> "#AARRGGBB" with the highlighter alpha.
    public static string ToStored(string rgbHex) => "#" + AlphaHex + rgbHex.TrimStart('#');
}
