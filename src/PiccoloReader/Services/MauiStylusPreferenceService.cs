using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Services;

public class MauiStylusPreferenceService : IStylusPreferenceService
{
    private const string DrawsDirectlyKey = "StylusDrawsDirectly";

    private readonly object _gate = new();

    // Read on every touch event, so it is cached. The constructor loads it
    // off the UI thread, so the first stylus touch normally finds it ready.
    private bool? _drawsDirectly;

    public MauiStylusPreferenceService()
    {
        _ = Task.Run(() => { _ = DrawsDirectly; _ = RecognizesShapes; });
    }

    public bool DrawsDirectly
    {
        get
        {
            lock (_gate)
            {
                return _drawsDirectly ??= Preferences.Default.Get(DrawsDirectlyKey, true);
            }
        }
        set
        {
            lock (_gate)
            {
                _drawsDirectly = value;
                Preferences.Default.Set(DrawsDirectlyKey, value);
            }
        }
    }

    private const string RecognizesShapesKey = "RecognizeShapes";
    private bool? _recognizesShapes;

    public bool RecognizesShapes
    {
        get
        {
            lock (_gate)
            {
                return _recognizesShapes ??= Preferences.Default.Get(RecognizesShapesKey, true);
            }
        }
        set
        {
            lock (_gate)
            {
                _recognizesShapes = value;
                Preferences.Default.Set(RecognizesShapesKey, value);
            }
        }
    }

    private const string ShapeHintsShownKey = "ShapeHintsShown";
    private int? _shapeHintsShown;

    public int ShapeHintsShown
    {
        get
        {
            lock (_gate)
            {
                return _shapeHintsShown ??= Preferences.Default.Get(ShapeHintsShownKey, 0);
            }
        }
        set
        {
            lock (_gate)
            {
                _shapeHintsShown = value;
                Preferences.Default.Set(ShapeHintsShownKey, value);
            }
        }
    }
}
