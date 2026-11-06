using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiAnnotationToolPreferenceService : IAnnotationToolPreferenceService
{
    private const string PencilColorKey = "ToolPencilColor";
    private const string PencilWidthKey = "ToolPencilWidth";
    private const string HighlighterColorKey = "ToolHighlighterColor";
    private const string HighlighterWidthKey = "ToolHighlighterWidth";
    private const string EraserRadiusKey = "ToolEraserRadius";
    // Same key the text color was saved under before, so existing choices are kept.
    private const string TextColorKey = "TextColorHex";

    private readonly object _gate = new();
    private AnnotationToolSettings? _cached;

    public MauiAnnotationToolPreferenceService()
    {
        // Loads the preferences file off the UI thread before the viewer asks.
        _ = Task.Run(Load);
    }

    public AnnotationToolSettings Load()
    {
        lock (_gate)
        {
            return _cached ??= Read();
        }
    }

    public void Save(AnnotationToolSettings settings)
    {
        var clean = settings.Sanitized();
        lock (_gate)
        {
            _cached = clean;
            var p = Preferences.Default;
            p.Set(PencilColorKey, clean.PencilColorHex);
            p.Set(PencilWidthKey, clean.PencilStrokeWidth);
            p.Set(HighlighterColorKey, clean.HighlighterColorHex);
            p.Set(HighlighterWidthKey, clean.HighlighterStrokeWidth);
            p.Set(EraserRadiusKey, clean.EraserRadius);
            p.Set(TextColorKey, clean.TextColorHex);
        }
    }

    private static AnnotationToolSettings Read()
    {
        try
        {
            var d = new AnnotationToolSettings();
            var p = Preferences.Default;
            return new AnnotationToolSettings
            {
                PencilColorHex = p.Get(PencilColorKey, d.PencilColorHex),
                PencilStrokeWidth = p.Get(PencilWidthKey, d.PencilStrokeWidth),
                HighlighterColorHex = p.Get(HighlighterColorKey, d.HighlighterColorHex),
                HighlighterStrokeWidth = p.Get(HighlighterWidthKey, d.HighlighterStrokeWidth),
                EraserRadius = p.Get(EraserRadiusKey, d.EraserRadius),
                TextColorHex = p.Get(TextColorKey, d.TextColorHex),
            }.Sanitized();
        }
        catch (Exception)
        {
            return new AnnotationToolSettings();
        }
    }
}
