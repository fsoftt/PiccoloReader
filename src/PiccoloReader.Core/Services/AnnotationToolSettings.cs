using System.Text.RegularExpressions;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

// The last-used options of the annotation tools, kept across app restarts.
// The active tool itself is not part of it: the viewer never opens in drawing mode.
public sealed record AnnotationToolSettings
{
    public const string DefaultColorHex = "#000000";
    public const double DefaultPencilStrokeWidth = 0.008;
    public const double MinPencilStrokeWidth = 0.002;
    public const double MaxPencilStrokeWidth = 0.02;
    public const double DefaultEraserRadius = 0.05;
    public const double MinEraserRadius = 0.02;
    public const double MaxEraserRadius = 0.08;

    private static readonly Regex HexColor = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    public string PencilColorHex { get; init; } = DefaultColorHex;
    public double PencilStrokeWidth { get; init; } = DefaultPencilStrokeWidth;
    public string HighlighterColorHex { get; init; } = HighlighterPalette.Default;
    public double HighlighterStrokeWidth { get; init; } = HighlighterPalette.DefaultStrokeWidth;
    public double EraserRadius { get; init; } = DefaultEraserRadius;
    public string TextColorHex { get; init; } = DefaultColorHex;

    // Replaces missing or invalid values with the defaults and clamps sizes to
    // what the sliders allow.
    public AnnotationToolSettings Sanitized() => new()
    {
        PencilColorHex = Color(PencilColorHex, DefaultColorHex),
        PencilStrokeWidth = Size(PencilStrokeWidth, DefaultPencilStrokeWidth, MinPencilStrokeWidth, MaxPencilStrokeWidth),
        HighlighterColorHex = Color(HighlighterColorHex, HighlighterPalette.Default),
        HighlighterStrokeWidth = Size(HighlighterStrokeWidth, HighlighterPalette.DefaultStrokeWidth, HighlighterPalette.MinStrokeWidth, HighlighterPalette.MaxStrokeWidth),
        EraserRadius = Size(EraserRadius, DefaultEraserRadius, MinEraserRadius, MaxEraserRadius),
        TextColorHex = Color(TextColorHex, DefaultColorHex),
    };

    private static string Color(string? value, string fallback) =>
        value is not null && HexColor.IsMatch(value) ? value : fallback;

    private static double Size(double value, double fallback, double min, double max) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);
}

public interface IAnnotationToolPreferenceService
{
    // Cached in memory after the first read; never throws.
    AnnotationToolSettings Load();

    void Save(AnnotationToolSettings settings);
}
